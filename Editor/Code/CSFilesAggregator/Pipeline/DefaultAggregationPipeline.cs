using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LegendaryTools.CSFilesAggregator.DependencyScan;
using LegendaryTools.CSFilesAggregator.TypeIndex;
using LegendaryTools.Editor.Code.CSFilesAggregator.Services;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Default file-system-based pipeline implementation.
    /// </summary>
    public sealed class DefaultAggregationPipeline : IAggregationPipeline
    {
        private readonly IPathService _pathService;
        private readonly IFileDiscovery _discovery;
        private readonly IFileReader _reader;
        private readonly IAggregationFormatter _formatter;

        /// <summary>
        /// Creates a new pipeline.
        /// </summary>
        public DefaultAggregationPipeline(
            IPathService pathService,
            IFileDiscovery discovery,
            IFileReader reader,
            IAggregationFormatter formatter)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        }

        /// <inheritdoc />
        public CSFilesAggregationResult Execute(CSFilesAggregationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            List<Diagnostic> diagnostics = new();
            StringBuilder sb = new();

            // Track processed absolute file paths to avoid duplicating content (inputs + dependencies).
            HashSet<string> processedAbsoluteFiles = new(StringComparer.OrdinalIgnoreCase);

            // Collect initial files processed from user selection. Used as dependency scan roots.
            List<PathResolution> initialFilesForDependencyScan = request.IncludeDependencies
                ? new List<PathResolution>(512)
                : null;

            // Process selected inputs normally (folders + files).
            for (int i = 0; i < request.InputPaths.Count; i++)
            {
                string input = request.InputPaths[i];
                PathResolution resolution = _pathService.Resolve(input);

                switch (resolution.Kind)
                {
                    case PathKind.File:
                        ProcessSingleFile(
                            sb,
                            resolution,
                            request.Transforms,
                            request.AppendDelimiters,
                            diagnostics,
                            processedAbsoluteFiles,
                            initialFilesForDependencyScan,
                            request.DoNotStripDisplayPaths);
                        break;

                    case PathKind.Folder:
                        ProcessFolder(
                            sb,
                            resolution,
                            request.IncludeSubfolders,
                            request.Transforms,
                            request.AppendDelimiters,
                            diagnostics,
                            processedAbsoluteFiles,
                            initialFilesForDependencyScan,
                            request.DoNotStripDisplayPaths);
                        break;

                    default:
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            $"Invalid path: {input}. It is neither a folder nor a .cs file.",
                            input));
                        _formatter.AppendInvalidPath(sb, input);
                        break;
                }
            }

            // Expand with dependencies after processing inputs.
            if (request.IncludeDependencies && initialFilesForDependencyScan != null &&
                initialFilesForDependencyScan.Count > 0)
                AppendDependencies(
                    sb,
                    initialFilesForDependencyScan,
                    request.DependencyScanSettings,
                    request.Transforms,
                    request.AppendDelimiters,
                    diagnostics,
                    processedAbsoluteFiles,
                    request.DoNotStripDisplayPaths);

            return new CSFilesAggregationResult(sb.ToString(), diagnostics);
        }

        private void AppendDependencies(
            StringBuilder sb,
            List<PathResolution> initialFiles,
            DependencyScanSettings settings,
            IReadOnlyList<ITextTransform> transforms,
            bool appendDelimiters,
            List<Diagnostic> diagnostics,
            HashSet<string> processedAbsoluteFiles,
            IReadOnlyCollection<string> doNotStripDisplayPaths)
        {
            // IMPORTANT: Enforce semantics: 0 = none, 1 = direct, 2 = direct + indirect, etc.
            // Some scanners treat depth 0 as "direct deps". We do not.
            if (settings == null || settings.MaxDepth <= 0) return;

            try
            {
                // Requirement: always rebuild to ensure the index is up to date.
                TypeIndex index = TypeIndexService.RebuildAndSave();

                string[] absolutePaths = initialFiles
                    .Where(f => f != null && !string.IsNullOrEmpty(f.AbsolutePath))
                    .Select(f => f.AbsolutePath)
                    .ToArray();

                string[] projectRelativePaths = initialFiles
                    .Where(f => f != null)
                    .Select(f => f.DisplayPath ?? string.Empty)
                    .ToArray();

                DependencyScanRequest request = new()
                {
                    AbsoluteFilePaths = absolutePaths,
                    ProjectRelativeFilePaths = projectRelativePaths,
                    InMemorySources = null
                };

                string[] dependentProjectRelativePaths =
                    CodeDependencyScanner.ScanDependentFilePaths(index, request, settings);

                if (dependentProjectRelativePaths == null || dependentProjectRelativePaths.Length == 0) return;

                // Process dependencies as files (no folder grouping).
                for (int i = 0; i < dependentProjectRelativePaths.Length; i++)
                {
                    string depPath = dependentProjectRelativePaths[i];
                    if (string.IsNullOrWhiteSpace(depPath)) continue;

                    PathResolution resolved = _pathService.Resolve(depPath);
                    if (resolved.Kind != PathKind.File)
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning,
                            $"Dependency path could not be resolved to a file: {depPath}",
                            depPath));

                        _formatter.AppendInvalidPath(sb, depPath);
                        _formatter.AppendSpacing(sb);
                        continue;
                    }

                    ProcessSingleFile(
                        sb,
                        resolved,
                        transforms,
                        appendDelimiters,
                        diagnostics,
                        processedAbsoluteFiles,
                        null,
                        doNotStripDisplayPaths);
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    $"Dependency scan failed: {ex.Message}",
                    null));

                _formatter.AppendFileReadError(sb, "DependencyScan", ex.Message);
                _formatter.AppendSpacing(sb);
            }
        }

        private void ProcessFolder(
            StringBuilder sb,
            PathResolution folder,
            bool includeSubfolders,
            IReadOnlyList<ITextTransform> transforms,
            bool appendDelimiters,
            List<Diagnostic> diagnostics,
            HashSet<string> processedAbsoluteFiles,
            List<PathResolution> initialFilesForDependencyScan,
            IReadOnlyCollection<string> doNotStripDisplayPaths)
        {
            IReadOnlyList<PathResolution> files = _discovery.DiscoverCsFiles(folder.AbsolutePath, includeSubfolders);

            if (files.Count == 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"No .cs files found in folder: {folder.DisplayPath}",
                    folder.DisplayPath));

                _formatter.AppendNoFilesInFolder(sb, folder.DisplayPath);
                _formatter.AppendSpacing(sb);
                return;
            }

            for (int i = 0; i < files.Count; i++)
            {
                ProcessSingleFile(
                    sb,
                    files[i],
                    transforms,
                    appendDelimiters,
                    diagnostics,
                    processedAbsoluteFiles,
                    initialFilesForDependencyScan,
                    doNotStripDisplayPaths);
            }

            if (appendDelimiters)
            {
                _formatter.AppendEndOfFolder(sb, folder.DisplayPath);
                _formatter.AppendSpacing(sb);
            }
        }

        private void ProcessSingleFile(
            StringBuilder sb,
            PathResolution file,
            IReadOnlyList<ITextTransform> transforms,
            bool appendDelimiters,
            List<Diagnostic> diagnostics,
            HashSet<string> processedAbsoluteFiles,
            List<PathResolution> initialFilesForDependencyScan,
            IReadOnlyCollection<string> doNotStripDisplayPaths)
        {
            if (file == null) return;

            if (!file.AbsolutePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Skipped non-.cs file: {file.DisplayPath}",
                    file.DisplayPath));

                _formatter.AppendSkippedNonCsFile(sb, file.DisplayPath);
                _formatter.AppendSpacing(sb);
                return;
            }

            // Avoid duplicates across selection and dependency expansion.
            if (processedAbsoluteFiles != null && !processedAbsoluteFiles.Add(file.AbsolutePath)) return;

            // Collect roots for dependency scanning (only the initial selection processing).
            if (initialFilesForDependencyScan != null) initialFilesForDependencyScan.Add(file);

            try
            {
                string content = _reader.ReadAllText(file.AbsolutePath);

                TextDocument doc = new(file.DisplayPath, content);

                if (transforms != null && transforms.Count > 0)
                {
                    bool shouldSkipImplementationStripper = doNotStripDisplayPaths != null
                                                            && !string.IsNullOrWhiteSpace(file.DisplayPath)
                                                            && doNotStripDisplayPaths.Contains(file.DisplayPath);

                    for (int i = 0; i < transforms.Count; i++)
                    {
                        ITextTransform transform = transforms[i];
                        if (transform == null) continue;

                        if (shouldSkipImplementationStripper && transform is ImplementationStripperTransform) continue;

                        doc = transform.Transform(doc, diagnostics);
                    }
                }

                _formatter.AppendFileContent(sb, doc);
                _formatter.AppendSpacing(sb);

                if (appendDelimiters)
                {
                    _formatter.AppendEndOfFile(sb, file.DisplayPath);
                    _formatter.AppendSpacing(sb);
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    $"An error occurred while processing file {file.DisplayPath}: {ex.Message}",
                    file.DisplayPath));

                _formatter.AppendFileReadError(sb, file.DisplayPath, ex.Message);
                _formatter.AppendSpacing(sb);
            }
        }
    }
}
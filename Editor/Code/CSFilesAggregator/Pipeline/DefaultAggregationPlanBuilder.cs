using System;
using System.Collections.Generic;
using System.Linq;
using LegendaryTools.CSFilesAggregator.DependencyScan;
using LegendaryTools.CSFilesAggregator.TypeIndex;
using LegendaryTools.Editor.Code.CSFilesAggregator.Services;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Default plan builder that mirrors the pipeline's path expansion behavior without reading file contents.
    /// </summary>
    public sealed class DefaultAggregationPlanBuilder : IAggregationPlanBuilder
    {
        private readonly IPathService _pathService;
        private readonly IFileDiscovery _discovery;

        /// <summary>
        /// Creates a new plan builder.
        /// </summary>
        public DefaultAggregationPlanBuilder(IPathService pathService, IFileDiscovery discovery)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        }

        /// <inheritdoc />
        public AggregationPlan Build(CSFilesAggregationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            List<Diagnostic> diagnostics = new();
            List<AggregationPlanFile> inputFiles = new(512);
            List<AggregationPlanFile> dependencyFiles = new(512);
            List<PathResolution> inputFileResolutionsForDependencyScan = request.IncludeDependencies
                ? new List<PathResolution>(512)
                : null;

            // De-duplicate by absolute file path (matches pipeline behavior; Windows safe).
            HashSet<string> processedAbsoluteFiles = new(StringComparer.OrdinalIgnoreCase);

            // Expand selected inputs.
            for (int i = 0; i < request.InputPaths.Count; i++)
            {
                string input = request.InputPaths[i];
                PathResolution resolution = _pathService.Resolve(input);

                switch (resolution.Kind)
                {
                    case PathKind.File:
                        AddFileIfValid(
                            resolution,
                            AggregationPlanFileSource.Input,
                            inputFiles,
                            diagnostics,
                            processedAbsoluteFiles,
                            inputFileResolutionsForDependencyScan);
                        break;

                    case PathKind.Folder:
                        AddFolderFiles(
                            resolution,
                            request.IncludeSubfolders,
                            inputFiles,
                            diagnostics,
                            processedAbsoluteFiles,
                            inputFileResolutionsForDependencyScan);
                        break;

                    default:
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            $"Invalid path: {input}. It is neither a folder nor a .cs file.",
                            input));
                        break;
                }
            }

            // Expand dependencies after inputs.
            if (request.IncludeDependencies && inputFileResolutionsForDependencyScan != null &&
                inputFileResolutionsForDependencyScan.Count > 0)
                AddDependencyFiles(
                    inputFileResolutionsForDependencyScan,
                    request.DependencyScanSettings,
                    dependencyFiles,
                    diagnostics,
                    processedAbsoluteFiles);

            // All files = inputs + dependencies, already de-duplicated by processedAbsoluteFiles,
            // but we still want a deterministic list. Preserve insertion order as built.
            List<AggregationPlanFile> allFiles = new(inputFiles.Count + dependencyFiles.Count);
            allFiles.AddRange(inputFiles);
            allFiles.AddRange(dependencyFiles);

            return new AggregationPlan(
                inputFiles,
                dependencyFiles,
                allFiles,
                diagnostics);
        }

        private void AddFolderFiles(
            PathResolution folder,
            bool includeSubfolders,
            List<AggregationPlanFile> output,
            List<Diagnostic> diagnostics,
            HashSet<string> processedAbsoluteFiles,
            List<PathResolution> inputFileResolutionsForDependencyScan)
        {
            IReadOnlyList<PathResolution> files = _discovery.DiscoverCsFiles(folder.AbsolutePath, includeSubfolders);
            if (files == null || files.Count == 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"No .cs files found in folder: {folder.DisplayPath}",
                    folder.DisplayPath));
                return;
            }

            for (int i = 0; i < files.Count; i++)
            {
                AddFileIfValid(
                    files[i],
                    AggregationPlanFileSource.Input,
                    output,
                    diagnostics,
                    processedAbsoluteFiles,
                    inputFileResolutionsForDependencyScan);
            }
        }

        private void AddDependencyFiles(
            List<PathResolution> inputFiles,
            DependencyScanSettings settings,
            List<AggregationPlanFile> dependencyFiles,
            List<Diagnostic> diagnostics,
            HashSet<string> processedAbsoluteFiles)
        {
            try
            {
                // Requirement: always rebuild to ensure the index is up to date.
                TypeIndex index = TypeIndexService.RebuildAndSave();

                string[] absolutePaths = inputFiles
                    .Where(f => f != null && !string.IsNullOrEmpty(f.AbsolutePath))
                    .Select(f => f.AbsolutePath)
                    .ToArray();

                string[] projectRelativePaths = inputFiles
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
                        continue;
                    }

                    AddFileIfValid(
                        resolved,
                        AggregationPlanFileSource.Dependency,
                        dependencyFiles,
                        diagnostics,
                        processedAbsoluteFiles,
                        null);
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    $"Dependency scan failed: {ex.Message}",
                    null));
            }
        }

        private static void AddFileIfValid(
            PathResolution file,
            AggregationPlanFileSource source,
            List<AggregationPlanFile> output,
            List<Diagnostic> diagnostics,
            HashSet<string> processedAbsoluteFiles,
            List<PathResolution> inputFileResolutionsForDependencyScan)
        {
            if (file == null) return;

            if (string.IsNullOrWhiteSpace(file.AbsolutePath) ||
                !file.AbsolutePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics?.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Skipped non-.cs file: {file.DisplayPath}",
                    file.DisplayPath));
                return;
            }

            if (processedAbsoluteFiles != null && !processedAbsoluteFiles.Add(file.AbsolutePath)) return;

            output?.Add(new AggregationPlanFile(file.AbsolutePath, file.DisplayPath, source));

            if (source == AggregationPlanFileSource.Input && inputFileResolutionsForDependencyScan != null)
                inputFileResolutionsForDependencyScan.Add(file);
        }
    }
}
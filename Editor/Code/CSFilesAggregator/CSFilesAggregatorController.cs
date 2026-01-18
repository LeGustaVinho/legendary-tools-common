using System;
using System.Collections.Generic;
using CSharpRegexStripper;
using LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline;
using LegendaryTools.Editor.Code.CSFilesAggregator.Services;

namespace LegendaryTools.Editor.Code.CSFilesAggregator
{
    /// <summary>
    /// Orchestrates UI intents and aggregation execution.
    /// </summary>
    public sealed class CSFilesAggregatorController
    {
        private readonly IFilePickerService _filePickerService;
        private readonly IPathService _pathService;
        private readonly IClipboardService _clipboardService;
        private readonly IEditorDialogService _dialogService;
        private readonly IAggregationPipeline _aggregationPipeline;
        private readonly ITextTransformsProvider _transformsProvider;
        private readonly ICSFilesAggregatorPersistence _persistence;

        /// <summary>
        /// Raised whenever <see cref="State"/> changes.
        /// </summary>
        public event Action StateChanged;

        /// <summary>
        /// Current view state.
        /// </summary>
        public CSFilesAggregatorState State { get; private set; }

        /// <summary>
        /// Creates a new controller instance.
        /// </summary>
        public CSFilesAggregatorController(
            IFilePickerService filePickerService,
            IPathService pathService,
            IClipboardService clipboardService,
            IEditorDialogService dialogService,
            IAggregationPipeline aggregationPipeline,
            ITextTransformsProvider transformsProvider,
            ICSFilesAggregatorPersistence persistence)
        {
            _filePickerService = filePickerService ?? throw new ArgumentNullException(nameof(filePickerService));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _aggregationPipeline = aggregationPipeline ?? throw new ArgumentNullException(nameof(aggregationPipeline));
            _transformsProvider = transformsProvider ?? throw new ArgumentNullException(nameof(transformsProvider));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));

            State = LoadInitialState();
        }

        /// <summary>
        /// Requests a folder selection and adds it if valid.
        /// </summary>
        public void RequestAddFolder()
        {
            string absolutePath = _filePickerService.OpenFolderPanel("Select folder", _pathService.AssetsAbsolutePath);
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return;
            }

            AddPath(absolutePath);
        }

        /// <summary>
        /// Requests a C# file selection and adds it if valid.
        /// </summary>
        public void RequestAddFile()
        {
            string absolutePath = _filePickerService.OpenFilePanel("Select .cs file", _pathService.AssetsAbsolutePath, "cs");
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return;
            }

            AddPath(absolutePath);
        }

        /// <summary>
        /// Adds paths coming from drag-and-drop.
        /// </summary>
        public void AddPathsFromDragAndDrop(IEnumerable<string> paths)
        {
            if (paths == null)
            {
                return;
            }

            bool changed = false;

            foreach (string p in paths)
            {
                changed |= TryAddPathNoNotify(p);
            }

            if (changed)
            {
                PersistSelectionAndSettings();
                RaiseStateChanged();
            }
        }

        /// <summary>
        /// Adds a folder or file path, normalizing it to a project-relative display path when possible.
        /// </summary>
        public void AddPath(string path)
        {
            if (TryAddPathNoNotify(path))
            {
                PersistSelectionAndSettings();
                RaiseStateChanged();
            }
        }

        /// <summary>
        /// Removes a path at the specified index.
        /// </summary>
        public void RemovePathAt(int index)
        {
            if (index < 0 || index >= State.Paths.Count)
            {
                return;
            }

            List<string> next = new List<string>(State.Paths);
            next.RemoveAt(index);

            State = State.WithPaths(next);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Updates include-subfolders flag.
        /// </summary>
        public void SetIncludeSubfolders(bool value)
        {
            if (State.IncludeSubfolders == value)
            {
                return;
            }

            State = State.WithIncludeSubfolders(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Updates remove-usings flag.
        /// </summary>
        public void SetRemoveUsings(bool value)
        {
            if (State.RemoveUsings == value)
            {
                return;
            }

            State = State.WithRemoveUsings(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Updates whether end markers ("End of file/folder") should be appended.
        /// </summary>
        public void SetAppendDelimiters(bool value)
        {
            if (State.AppendDelimiters == value)
            {
                return;
            }

            State = State.WithAppendDelimiters(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Enables/disables the implementation stripper transform.
        /// </summary>
        public void SetUseImplementationStripper(bool value)
        {
            if (State.UseImplementationStripper == value)
            {
                return;
            }

            State = State.WithUseImplementationStripper(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Sets stripper method body mode.
        /// </summary>
        public void SetStripMethodBodyMode(MethodBodyMode value)
        {
            if (State.StripMethodBodyMode == value)
            {
                return;
            }

            State = State.WithStripMethodBodyMode(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Sets whether non-auto properties should be converted to auto-properties.
        /// </summary>
        public void SetStripConvertNonAutoProperties(bool value)
        {
            if (State.StripConvertNonAutoProperties == value)
            {
                return;
            }

            State = State.WithStripConvertNonAutoProperties(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Sets whether strings and comments should be masked before stripping.
        /// </summary>
        public void SetStripMaskStringsAndComments(bool value)
        {
            if (State.StripMaskStringsAndComments == value)
            {
                return;
            }

            State = State.WithStripMaskStringsAndComments(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Sets whether interface members should be skipped during stripping.
        /// </summary>
        public void SetStripSkipInterfaceMembers(bool value)
        {
            if (State.StripSkipInterfaceMembers == value)
            {
                return;
            }

            State = State.WithStripSkipInterfaceMembers(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Sets whether abstract members should be skipped during stripping.
        /// </summary>
        public void SetStripSkipAbstractMembers(bool value)
        {
            if (State.StripSkipAbstractMembers == value)
            {
                return;
            }

            State = State.WithStripSkipAbstractMembers(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Enables/disables dependency scanning expansion.
        /// </summary>
        public void SetIncludeDependencies(bool value)
        {
            if (State.IncludeDependencies == value)
            {
                return;
            }

            State = State.WithIncludeDependencies(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Sets dependency scan max depth.
        /// </summary>
        public void SetDependencyMaxDepth(int value)
        {
            value = Clamp(value, 0, 50);
            if (State.DependencyMaxDepth == value)
            {
                return;
            }

            State = State.WithDependencyMaxDepth(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Sets whether Packages/ should be ignored when scanning dependencies.
        /// </summary>
        public void SetDependencyIgnorePackagesFolder(bool value)
        {
            if (State.DependencyIgnorePackagesFolder == value)
            {
                return;
            }

            State = State.WithDependencyIgnorePackagesFolder(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Sets whether Library/PackageCache/ should be ignored when scanning dependencies.
        /// </summary>
        public void SetDependencyIgnorePackageCache(bool value)
        {
            if (State.DependencyIgnorePackageCache == value)
            {
                return;
            }

            State = State.WithDependencyIgnorePackageCache(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Sets whether unresolved types should be ignored.
        /// </summary>
        public void SetDependencyIgnoreUnresolvedTypes(bool value)
        {
            if (State.DependencyIgnoreUnresolvedTypes == value)
            {
                return;
            }

            State = State.WithDependencyIgnoreUnresolvedTypes(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Sets whether to include the input files in the dependency result.
        /// </summary>
        public void SetDependencyIncludeInputFilesInResult(bool value)
        {
            if (State.DependencyIncludeInputFilesInResult == value)
            {
                return;
            }

            State = State.WithDependencyIncludeInputFilesInResult(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Sets whether in-memory virtual paths should be included.
        /// </summary>
        public void SetDependencyIncludeInMemoryVirtualPathsInResult(bool value)
        {
            if (State.DependencyIncludeInMemoryVirtualPathsInResult == value)
            {
                return;
            }

            State = State.WithDependencyIncludeInMemoryVirtualPathsInResult(value);

            PersistSelectionAndSettings();
            RaiseStateChanged();
        }

        /// <summary>
        /// Updates aggregated text (editable in the UI). Not persisted by design.
        /// </summary>
        public void SetAggregatedText(string text)
        {
            text ??= string.Empty;

            if (string.Equals(State.AggregatedText, text, StringComparison.Ordinal))
            {
                return;
            }

            State = State.WithAggregatedText(text);
            RaiseStateChanged();
        }

        /// <summary>
        /// Copies the current aggregated text to the clipboard.
        /// </summary>
        public void CopyAggregatedTextToClipboard()
        {
            _clipboardService.SetClipboardText(State.AggregatedText ?? string.Empty);
            _dialogService.ShowDialog("Copied", "Aggregated text copied to clipboard.", "OK");
        }

        /// <summary>
        /// Runs aggregation and updates output text.
        /// </summary>
        public void Aggregate()
        {
            if (State.Paths.Count == 0)
            {
                _dialogService.ShowDialog("Error", "Please add at least one folder or .cs file.", "OK");
                return;
            }

            IReadOnlyList<ITextTransform> transforms = _transformsProvider.BuildTransforms(State);

            var dependencySettings = State.BuildDependencyScanSettings();

            CSFilesAggregationRequest request = new CSFilesAggregationRequest(
                inputPaths: State.Paths,
                includeSubfolders: State.IncludeSubfolders,
                appendDelimiters: State.AppendDelimiters,
                includeDependencies: State.IncludeDependencies,
                dependencyScanSettings: dependencySettings,
                transforms: transforms);

            CSFilesAggregationResult result = _aggregationPipeline.Execute(request);

            State = State.WithAggregatedText(result.AggregatedText ?? string.Empty);
            RaiseStateChanged();

            _clipboardService.SetClipboardText(State.AggregatedText);

            bool hasErrors = result.Diagnostics != null && result.Diagnostics.HasSeverity(DiagnosticSeverity.Error);
            if (hasErrors)
            {
                _dialogService.ShowDialog(
                    "Completed with Errors",
                    "Aggregation completed, but some files/paths failed. See output comments and logs.",
                    "OK");
                return;
            }

            _dialogService.ShowDialog("Success", "The .cs files have been aggregated successfully!", "OK");
        }

        private CSFilesAggregatorState LoadInitialState()
        {
            CSFilesAggregatorPersistedData persisted = _persistence.Load();

            CSFilesAggregatorState state = CSFilesAggregatorState.CreateDefault()
                .WithIncludeSubfolders(persisted.IncludeSubfolders)
                .WithRemoveUsings(persisted.RemoveUsings)
                .WithAppendDelimiters(persisted.AppendDelimiters)
                .WithUseImplementationStripper(persisted.UseImplementationStripper)
                .WithStripMethodBodyMode(persisted.StripMethodBodyMode)
                .WithStripConvertNonAutoProperties(persisted.StripConvertNonAutoProperties)
                .WithStripMaskStringsAndComments(persisted.StripMaskStringsAndComments)
                .WithStripSkipInterfaceMembers(persisted.StripSkipInterfaceMembers)
                .WithStripSkipAbstractMembers(persisted.StripSkipAbstractMembers)
                .WithIncludeDependencies(persisted.IncludeDependencies)
                .WithDependencyMaxDepth(persisted.DependencyMaxDepth)
                .WithDependencyIgnorePackagesFolder(persisted.DependencyIgnorePackagesFolder)
                .WithDependencyIgnorePackageCache(persisted.DependencyIgnorePackageCache)
                .WithDependencyIgnoreUnresolvedTypes(persisted.DependencyIgnoreUnresolvedTypes)
                .WithDependencyIncludeInputFilesInResult(persisted.DependencyIncludeInputFilesInResult)
                .WithDependencyIncludeInMemoryVirtualPathsInResult(persisted.DependencyIncludeInMemoryVirtualPathsInResult);

            if (persisted.Paths != null && persisted.Paths.Count > 0)
            {
                // Normalize and de-duplicate on load.
                List<string> normalized = new List<string>(persisted.Paths.Count);
                for (int i = 0; i < persisted.Paths.Count; i++)
                {
                    string p = persisted.Paths[i];
                    if (string.IsNullOrWhiteSpace(p))
                    {
                        continue;
                    }

                    string display = _pathService.NormalizeToProjectDisplayPath(p);
                    if (!ContainsPath(normalized, display))
                    {
                        normalized.Add(display);
                    }
                }

                state = state.WithPaths(normalized);
            }

            return state;
        }

        private bool TryAddPathNoNotify(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string displayPath = _pathService.NormalizeToProjectDisplayPath(path);

            if (ContainsPath(State.Paths, displayPath))
            {
                return false;
            }

            List<string> next = new List<string>(State.Paths) { displayPath };
            State = State.WithPaths(next);
            return true;
        }

        private void PersistSelectionAndSettings()
        {
            CSFilesAggregatorPersistedData data = new CSFilesAggregatorPersistedData
            {
                IncludeSubfolders = State.IncludeSubfolders,
                RemoveUsings = State.RemoveUsings,
                AppendDelimiters = State.AppendDelimiters,
                UseImplementationStripper = State.UseImplementationStripper,
                StripMethodBodyMode = State.StripMethodBodyMode,
                StripConvertNonAutoProperties = State.StripConvertNonAutoProperties,
                StripMaskStringsAndComments = State.StripMaskStringsAndComments,
                StripSkipInterfaceMembers = State.StripSkipInterfaceMembers,
                StripSkipAbstractMembers = State.StripSkipAbstractMembers,
                IncludeDependencies = State.IncludeDependencies,
                DependencyMaxDepth = State.DependencyMaxDepth,
                DependencyIgnorePackagesFolder = State.DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache = State.DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes = State.DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult = State.DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult = State.DependencyIncludeInMemoryVirtualPathsInResult,
                Paths = new List<string>(State.Paths)
            };

            _persistence.Save(data);
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }

        private static bool ContainsPath(IReadOnlyList<string> paths, string value)
        {
            if (paths == null || paths.Count == 0)
            {
                return false;
            }

            if (value == null)
            {
                return false;
            }

            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}

using System.Collections.Generic;
using CSharpRegexStripper;
using LegendaryTools.CSFilesAggregator.DependencyScan;

namespace LegendaryTools.Editor.Code.CSFilesAggregator
{
    /// <summary>
    /// Immutable state model for the CS files aggregator UI.
    /// </summary>
    public sealed class CSFilesAggregatorState
    {
        /// <summary>
        /// Selected folder and file paths (project display paths when possible).
        /// </summary>
        public IReadOnlyList<string> Paths { get; }

        /// <summary>
        /// Whether subfolders should be included during folder discovery.
        /// </summary>
        public bool IncludeSubfolders { get; }

        /// <summary>
        /// Whether C# 'using' directives should be removed from file content.
        /// </summary>
        public bool RemoveUsings { get; }

        /// <summary>
        /// Whether end markers (e.g., "End of file/folder") should be appended.
        /// </summary>
        public bool AppendDelimiters { get; }

        /// <summary>
        /// Whether the implementation stripper should run as a transform.
        /// </summary>
        public bool UseImplementationStripper { get; }

        /// <summary>
        /// Stripper: method body mode.
        /// </summary>
        public MethodBodyMode StripMethodBodyMode { get; }

        /// <summary>
        /// Stripper: convert non-auto get/set properties to auto-properties.
        /// </summary>
        public bool StripConvertNonAutoProperties { get; }

        /// <summary>
        /// Stripper: mask strings/comments before stripping.
        /// </summary>
        public bool StripMaskStringsAndComments { get; }

        /// <summary>
        /// Stripper: skip interface members.
        /// </summary>
        public bool StripSkipInterfaceMembers { get; }

        /// <summary>
        /// Stripper: skip abstract members.
        /// </summary>
        public bool StripSkipAbstractMembers { get; }

        /// <summary>
        /// Whether dependency scan should expand selected inputs.
        /// </summary>
        public bool IncludeDependencies { get; }

        /// <summary>
        /// Dependency scan: max traversal depth.
        /// </summary>
        public int DependencyMaxDepth { get; }

        /// <summary>
        /// Dependency scan: ignore Packages/ folder.
        /// </summary>
        public bool DependencyIgnorePackagesFolder { get; }

        /// <summary>
        /// Dependency scan: ignore Library/PackageCache.
        /// </summary>
        public bool DependencyIgnorePackageCache { get; }

        /// <summary>
        /// Dependency scan: ignore unresolved types.
        /// </summary>
        public bool DependencyIgnoreUnresolvedTypes { get; }

        /// <summary>
        /// Dependency scan: include input files in result.
        /// </summary>
        public bool DependencyIncludeInputFilesInResult { get; }

        /// <summary>
        /// Dependency scan: include in-memory virtual paths.
        /// </summary>
        public bool DependencyIncludeInMemoryVirtualPathsInResult { get; }

        /// <summary>
        /// Aggregated output text.
        /// </summary>
        public string AggregatedText { get; }

        private CSFilesAggregatorState(
            IReadOnlyList<string> paths,
            bool includeSubfolders,
            bool removeUsings,
            bool appendDelimiters,
            bool useImplementationStripper,
            MethodBodyMode stripMethodBodyMode,
            bool stripConvertNonAutoProperties,
            bool stripMaskStringsAndComments,
            bool stripSkipInterfaceMembers,
            bool stripSkipAbstractMembers,
            bool includeDependencies,
            int dependencyMaxDepth,
            bool dependencyIgnorePackagesFolder,
            bool dependencyIgnorePackageCache,
            bool dependencyIgnoreUnresolvedTypes,
            bool dependencyIncludeInputFilesInResult,
            bool dependencyIncludeInMemoryVirtualPathsInResult,
            string aggregatedText)
        {
            Paths = paths ?? new List<string>();
            IncludeSubfolders = includeSubfolders;
            RemoveUsings = removeUsings;
            AppendDelimiters = appendDelimiters;

            UseImplementationStripper = useImplementationStripper;
            StripMethodBodyMode = stripMethodBodyMode;
            StripConvertNonAutoProperties = stripConvertNonAutoProperties;
            StripMaskStringsAndComments = stripMaskStringsAndComments;
            StripSkipInterfaceMembers = stripSkipInterfaceMembers;
            StripSkipAbstractMembers = stripSkipAbstractMembers;

            IncludeDependencies = includeDependencies;
            DependencyMaxDepth = dependencyMaxDepth;
            DependencyIgnorePackagesFolder = dependencyIgnorePackagesFolder;
            DependencyIgnorePackageCache = dependencyIgnorePackageCache;
            DependencyIgnoreUnresolvedTypes = dependencyIgnoreUnresolvedTypes;
            DependencyIncludeInputFilesInResult = dependencyIncludeInputFilesInResult;
            DependencyIncludeInMemoryVirtualPathsInResult = dependencyIncludeInMemoryVirtualPathsInResult;

            AggregatedText = aggregatedText ?? string.Empty;
        }

        /// <summary>
        /// Creates the default initial state.
        /// </summary>
        public static CSFilesAggregatorState CreateDefault()
        {
            StripOptions defaults = StripOptions.Default;

            return new CSFilesAggregatorState(
                paths: new List<string>(),
                includeSubfolders: false,
                removeUsings: false,
                appendDelimiters: true,
                useImplementationStripper: false,
                stripMethodBodyMode: defaults.MethodBodyMode,
                stripConvertNonAutoProperties: defaults.ConvertNonAutoGetSetPropertiesToAutoProperties,
                stripMaskStringsAndComments: defaults.MaskStringsAndCommentsBeforeStripping,
                stripSkipInterfaceMembers: defaults.SkipInterfaceMembers,
                stripSkipAbstractMembers: defaults.SkipAbstractMembers,
                includeDependencies: false,
                dependencyMaxDepth: 3,
                dependencyIgnorePackagesFolder: true,
                dependencyIgnorePackageCache: true,
                dependencyIgnoreUnresolvedTypes: true,
                dependencyIncludeInputFilesInResult: false,
                dependencyIncludeInMemoryVirtualPathsInResult: true,
                aggregatedText: string.Empty);
        }

        /// <summary>
        /// Builds a dependency scan settings object from the current UI state.
        /// </summary>
        public DependencyScanSettings BuildDependencyScanSettings()
        {
            return new DependencyScanSettings
            {
                MaxDepth = DependencyMaxDepth,
                IgnorePackagesFolder = DependencyIgnorePackagesFolder,
                IgnorePackageCache = DependencyIgnorePackageCache,
                IgnoreUnresolvedTypes = DependencyIgnoreUnresolvedTypes,
                IncludeInputFilesInResult = DependencyIncludeInputFilesInResult,
                IncludeInMemoryVirtualPathsInResult = DependencyIncludeInMemoryVirtualPathsInResult,
            };
        }

        /// <summary>
        /// Builds implementation stripper options from the current UI state.
        /// </summary>
        public StripOptions BuildStripOptions()
        {
            return new StripOptions(
                methodBodyMode: StripMethodBodyMode,
                convertNonAutoGetSetPropertiesToAutoProperties: StripConvertNonAutoProperties,
                maskStringsAndCommentsBeforeStripping: StripMaskStringsAndComments,
                skipInterfaceMembers: StripSkipInterfaceMembers,
                skipAbstractMembers: StripSkipAbstractMembers);
        }

        /// <summary>
        /// Returns a copy with updated paths.
        /// </summary>
        public CSFilesAggregatorState WithPaths(IReadOnlyList<string> paths)
        {
            return new CSFilesAggregatorState(
                paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithIncludeSubfolders(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                value,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithRemoveUsings(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                value,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithAppendDelimiters(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                value,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithUseImplementationStripper(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                value,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithStripMethodBodyMode(MethodBodyMode value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                value,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithStripConvertNonAutoProperties(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                value,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithStripMaskStringsAndComments(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                value,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithStripSkipInterfaceMembers(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                value,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithStripSkipAbstractMembers(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                value,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithIncludeDependencies(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                value,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithDependencyMaxDepth(int value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                value,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithDependencyIgnorePackagesFolder(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                value,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithDependencyIgnorePackageCache(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                value,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithDependencyIgnoreUnresolvedTypes(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                value,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithDependencyIncludeInputFilesInResult(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                value,
                DependencyIncludeInMemoryVirtualPathsInResult,
                AggregatedText);
        }

        public CSFilesAggregatorState WithDependencyIncludeInMemoryVirtualPathsInResult(bool value)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                value,
                AggregatedText);
        }

        public CSFilesAggregatorState WithAggregatedText(string text)
        {
            return new CSFilesAggregatorState(
                Paths,
                IncludeSubfolders,
                RemoveUsings,
                AppendDelimiters,
                UseImplementationStripper,
                StripMethodBodyMode,
                StripConvertNonAutoProperties,
                StripMaskStringsAndComments,
                StripSkipInterfaceMembers,
                StripSkipAbstractMembers,
                IncludeDependencies,
                DependencyMaxDepth,
                DependencyIgnorePackagesFolder,
                DependencyIgnorePackageCache,
                DependencyIgnoreUnresolvedTypes,
                DependencyIncludeInputFilesInResult,
                DependencyIncludeInMemoryVirtualPathsInResult,
                text);
        }
    }
}

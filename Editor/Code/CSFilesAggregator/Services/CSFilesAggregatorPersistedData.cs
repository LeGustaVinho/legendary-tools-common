using System.Collections.Generic;
using CSharpRegexStripper;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// Serializable data contract for persisted settings and selections.
    /// </summary>
    public sealed class CSFilesAggregatorPersistedData
    {
        /// <summary>
        /// Selected folder and file paths (display or absolute; will be normalized on load).
        /// </summary>
        public List<string> Paths { get; set; } = new();

        /// <summary>
        /// Whether folder discovery includes subfolders.
        /// </summary>
        public bool IncludeSubfolders { get; set; }

        /// <summary>
        /// Whether to remove using directives.
        /// </summary>
        public bool RemoveUsings { get; set; }

        /// <summary>
        /// Whether end markers (e.g., "End of file/folder") should be appended.
        /// </summary>
        public bool AppendDelimiters { get; set; } = true;

        /// <summary>
        /// Whether the implementation stripper should run as a transform.
        /// </summary>
        public bool UseImplementationStripper { get; set; }

        /// <summary>
        /// Stripper: method body mode.
        /// </summary>
        public MethodBodyMode StripMethodBodyMode { get; set; } = StripOptions.Default.MethodBodyMode;

        /// <summary>
        /// Stripper: convert non-auto properties to auto-properties.
        /// </summary>
        public bool StripConvertNonAutoProperties { get; set; } =
            StripOptions.Default.ConvertNonAutoGetSetPropertiesToAutoProperties;

        /// <summary>
        /// Stripper: mask strings & comments before stripping.
        /// </summary>
        public bool StripMaskStringsAndComments { get; set; } =
            StripOptions.Default.MaskStringsAndCommentsBeforeStripping;

        /// <summary>
        /// Stripper: skip interface members.
        /// </summary>
        public bool StripSkipInterfaceMembers { get; set; } = StripOptions.Default.SkipInterfaceMembers;

        /// <summary>
        /// Stripper: skip abstract members.
        /// </summary>
        public bool StripSkipAbstractMembers { get; set; } = StripOptions.Default.SkipAbstractMembers;

        /// <summary>
        /// Whether dependency scan should expand selected inputs.
        /// </summary>
        public bool IncludeDependencies { get; set; }

        /// <summary>
        /// Dependency scan: max traversal depth.
        /// </summary>
        public int DependencyMaxDepth { get; set; } = 3;

        /// <summary>
        /// Dependency scan: ignore Packages/.
        /// </summary>
        public bool DependencyIgnorePackagesFolder { get; set; } = true;

        /// <summary>
        /// Dependency scan: ignore Library/PackageCache/.
        /// </summary>
        public bool DependencyIgnorePackageCache { get; set; } = true;

        /// <summary>
        /// Dependency scan: ignore unresolved types.
        /// </summary>
        public bool DependencyIgnoreUnresolvedTypes { get; set; } = true;

        /// <summary>
        /// Dependency scan: include input files in result.
        /// </summary>
        public bool DependencyIncludeInputFilesInResult { get; set; }

        /// <summary>
        /// Dependency scan: include in-memory virtual paths.
        /// </summary>
        public bool DependencyIncludeInMemoryVirtualPathsInResult { get; set; } = true;
    }
}
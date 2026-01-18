// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/DependencyScanSettings.cs
using System;
using System.Collections.Generic;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Settings for scanning code dependencies starting from one or more C# sources.
    /// </summary>
    [Serializable]
    public sealed class DependencyScanSettings
    {
        /// <summary>
        /// Gets or sets the maximum traversal depth.
        /// Depth 0 means: no dependencies (empty result unless <see cref="IncludeInputFilesInResult"/> is true).
        /// Depth 1 means: direct dependencies, etc.
        /// </summary>
        public int MaxDepth = 3;

        /// <summary>
        /// Gets or sets a value indicating whether dependencies located under "Packages/" should be ignored.
        /// </summary>
        public bool IgnorePackagesFolder = true;

        /// <summary>
        /// Gets or sets a value indicating whether dependencies under "Library/PackageCache/" should be ignored.
        /// </summary>
        public bool IgnorePackageCache = true;

        /// <summary>
        /// Gets or sets additional project-relative path prefixes to ignore (e.g. "Assets/ThirdParty/").
        /// Prefix comparison is ordinal-ignore-case.
        /// </summary>
        public List<string> IgnoredPathPrefixes = new List<string>();

        /// <summary>
        /// Gets or sets a value indicating whether unresolved types should be treated as external
        /// (DLL/Unity/BCL) and therefore ignored.
        /// </summary>
        public bool IgnoreUnresolvedTypes = true;

        /// <summary>
        /// Gets or sets a value indicating whether to include the input files in the returned dependency list.
        /// </summary>
        public bool IncludeInputFilesInResult = false;

        /// <summary>
        /// Gets or sets a value indicating whether virtual in-memory paths should be included in results
        /// (when in-memory sources declare types that are depended upon).
        /// </summary>
        public bool IncludeInMemoryVirtualPathsInResult = true;
    }
}

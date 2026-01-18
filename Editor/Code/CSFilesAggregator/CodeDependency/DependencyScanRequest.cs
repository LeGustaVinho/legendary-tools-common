// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/DependencyScanRequest.cs
using System;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Input payload for a dependency scan.
    /// Supports scanning by file paths and/or in-memory sources.
    /// </summary>
    [Serializable]
    public sealed class DependencyScanRequest
    {
        /// <summary>
        /// Gets or sets the absolute file paths to scan.
        /// When provided, the scanner will read and parse these files.
        /// </summary>
        public string[] AbsoluteFilePaths;

        /// <summary>
        /// Gets or sets the project-relative paths matching <see cref="AbsoluteFilePaths"/> indices.
        /// If null/empty or missing an index, the scanner will compute a best-effort relative path.
        /// </summary>
        public string[] ProjectRelativeFilePaths;

        /// <summary>
        /// Gets or sets optional in-memory C# sources to scan.
        /// </summary>
        public InMemorySource[] InMemorySources;
    }
}

// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/DependencyScan/DependencyScanResult.cs
using System;
using System.Collections.Generic;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Result payload for a dependency scan.
    /// </summary>
    [Serializable]
    public sealed class DependencyScanResult
    {
        /// <summary>
        /// Gets the project-relative dependent file paths discovered by the scan.
        /// The list is unique (no duplicates).
        /// </summary>
        public List<string> DependentFilePaths = new List<string>();

        /// <summary>
        /// Gets optional notes about resolution, ambiguity, or filtering.
        /// </summary>
        public List<string> Notes = new List<string>();
    }
}

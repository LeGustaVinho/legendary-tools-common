using System;

namespace LegendaryTools.CSFilesAggregator.DependencyScan
{
    /// <summary>
    /// Represents a C# source provided in-memory instead of coming from a file.
    /// </summary>
    [Serializable]
    public sealed class InMemorySource
    {
        /// <summary>
        /// Gets or sets a stable identifier for the in-memory source (e.g. "SelectionBuffer.cs").
        /// </summary>
        public string InMemorySourceId;

        /// <summary>
        /// Gets or sets the C# code text.
        /// </summary>
        public string Code;

        /// <summary>
        /// Gets or sets an optional "virtual" project-relative path used for reporting and dependency output.
        /// If null/empty, <see cref="InMemorySourceId"/> is used.
        /// </summary>
        public string VirtualProjectRelativePath;
    }
}
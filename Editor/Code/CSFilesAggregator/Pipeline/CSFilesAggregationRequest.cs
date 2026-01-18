using System;
using System.Collections.Generic;
using LegendaryTools.CSFilesAggregator.DependencyScan;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Request model for C# file aggregation.
    /// </summary>
    public sealed class CSFilesAggregationRequest
    {
        /// <summary>
        /// Input paths (folders or .cs files). Prefer project display paths when possible (e.g., Assets/...).
        /// </summary>
        public IReadOnlyList<string> InputPaths { get; }

        /// <summary>
        /// Whether folder discovery includes subfolders.
        /// </summary>
        public bool IncludeSubfolders { get; }

        /// <summary>
        /// Whether end markers (e.g., "End of file/folder") should be appended.
        /// </summary>
        public bool AppendDelimiters { get; }

        /// <summary>
        /// Whether dependency scan should expand selected inputs.
        /// </summary>
        public bool IncludeDependencies { get; }

        /// <summary>
        /// Settings used for dependency scanning.
        /// </summary>
        public DependencyScanSettings DependencyScanSettings { get; }

        /// <summary>
        /// Transform steps applied to each file's text.
        /// </summary>
        public IReadOnlyList<ITextTransform> Transforms { get; }

        /// <summary>
        /// Creates a new request.
        /// </summary>
        public CSFilesAggregationRequest(
            IReadOnlyList<string> inputPaths,
            bool includeSubfolders,
            bool appendDelimiters,
            bool includeDependencies,
            DependencyScanSettings dependencyScanSettings,
            IReadOnlyList<ITextTransform> transforms)
        {
            InputPaths = inputPaths ?? throw new ArgumentNullException(nameof(inputPaths));
            IncludeSubfolders = includeSubfolders;
            AppendDelimiters = appendDelimiters;
            IncludeDependencies = includeDependencies;
            DependencyScanSettings = dependencyScanSettings ?? new DependencyScanSettings();
            Transforms = transforms ?? Array.Empty<ITextTransform>();
        }
    }
}

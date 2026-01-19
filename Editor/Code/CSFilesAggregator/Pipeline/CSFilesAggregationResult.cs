using System.Collections.Generic;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Result model for C# file aggregation.
    /// </summary>
    public sealed class CSFilesAggregationResult
    {
        /// <summary>
        /// Final aggregated output.
        /// </summary>
        public string AggregatedText { get; }

        /// <summary>
        /// Diagnostics emitted during discovery, reading, transforms, and formatting.
        /// </summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>
        /// Creates a new result.
        /// </summary>
        public CSFilesAggregationResult(string aggregatedText, IReadOnlyList<Diagnostic> diagnostics)
        {
            AggregatedText = aggregatedText ?? string.Empty;
            Diagnostics = diagnostics ?? new List<Diagnostic>();
        }
    }
}
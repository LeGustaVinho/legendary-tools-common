namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Represents a pipeline diagnostic message.
    /// </summary>
    public sealed class Diagnostic
    {
        /// <summary>
        /// Severity of the diagnostic.
        /// </summary>
        public DiagnosticSeverity Severity { get; }

        /// <summary>
        /// Human-readable message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Optional related path (input path or file path).
        /// </summary>
        public string RelatedPath { get; }

        /// <summary>
        /// Creates a new diagnostic.
        /// </summary>
        public Diagnostic(DiagnosticSeverity severity, string message, string relatedPath = null)
        {
            Severity = severity;
            Message = message ?? string.Empty;
            RelatedPath = relatedPath;
        }
    }
}
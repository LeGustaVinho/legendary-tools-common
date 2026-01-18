using System.Collections.Generic;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Helper extensions for diagnostics.
    /// </summary>
    public static class DiagnosticsExtensions
    {
        /// <summary>
        /// Returns true if any diagnostic matches the provided severity.
        /// </summary>
        public static bool HasSeverity(this IReadOnlyList<Diagnostic> diagnostics, DiagnosticSeverity severity)
        {
            if (diagnostics == null)
            {
                return false;
            }

            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i] != null && diagnostics[i].Severity == severity)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

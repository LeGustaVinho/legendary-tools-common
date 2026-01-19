using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Removes C# using directives from a document.
    /// </summary>
    public sealed class RemoveUsingsTransform : ITextTransform
    {
        /// <inheritdoc />
        public TextDocument Transform(TextDocument document, List<Diagnostic> diagnostics)
        {
            if (document == null) return document;

            string text = document.Text ?? string.Empty;

            try
            {
                string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                IEnumerable<string> filtered =
                    lines.Where(line => !line.TrimStart().StartsWith("using ", StringComparison.Ordinal));
                string updated = string.Join("\n", filtered);

                return document.WithText(updated);
            }
            catch (Exception ex)
            {
                diagnostics?.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Failed to remove usings for {document.DisplayPath}: {ex.Message}",
                    document.DisplayPath));

                return document;
            }
        }
    }
}
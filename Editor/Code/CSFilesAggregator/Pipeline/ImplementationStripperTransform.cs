using System;
using System.Collections.Generic;
using CSharpRegexStripper;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Applies <see cref="CSharpImplementationStripper"/> to strip implementation details from C# source text.
    /// </summary>
    public sealed class ImplementationStripperTransform : ITextTransform
    {
        private readonly StripOptions _options;

        /// <summary>
        /// Creates a new transform instance.
        /// </summary>
        public ImplementationStripperTransform(StripOptions options)
        {
            _options = options;
        }

        /// <inheritdoc />
        public TextDocument Transform(TextDocument document, List<Diagnostic> diagnostics)
        {
            if (document == null) return document;

            try
            {
                string input = document.Text ?? string.Empty;
                string output = CSharpImplementationStripper.StripFromString(input, _options) ?? string.Empty;
                return document.WithText(output);
            }
            catch (Exception ex)
            {
                diagnostics?.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Failed to strip implementation for {document.DisplayPath}: {ex.Message}",
                    document.DisplayPath));

                return document;
            }
        }
    }
}
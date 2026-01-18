using System.Collections.Generic;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Defines a transform step applied to a text document.
    /// </summary>
    public interface ITextTransform
    {
        /// <summary>
        /// Applies a transformation to <paramref name="document"/>.
        /// </summary>
        TextDocument Transform(TextDocument document, List<Diagnostic> diagnostics);
    }
}

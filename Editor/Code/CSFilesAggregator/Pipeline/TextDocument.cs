namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// In-memory representation of a text file being processed.
    /// </summary>
    public sealed class TextDocument
    {
        /// <summary>
        /// Display path (preferably project-relative).
        /// </summary>
        public string DisplayPath { get; }

        /// <summary>
        /// Text content.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Creates a new document.
        /// </summary>
        public TextDocument(string displayPath, string text)
        {
            DisplayPath = displayPath ?? string.Empty;
            Text = text ?? string.Empty;
        }

        /// <summary>
        /// Returns a copy with updated text.
        /// </summary>
        public TextDocument WithText(string text)
        {
            return new TextDocument(DisplayPath, text);
        }
    }
}

using System.Text;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Default formatter that mirrors the previous tool behavior while keeping output consistent.
    /// </summary>
    public sealed class DefaultAggregationFormatter : IAggregationFormatter
    {
        /// <inheritdoc />
        public void AppendFileContent(StringBuilder sb, TextDocument document)
        {
            if (sb == null || document == null) return;

            sb.AppendLine(document.Text ?? string.Empty);
        }

        /// <inheritdoc />
        public void AppendEndOfFile(StringBuilder sb, string displayPath)
        {
            if (sb == null) return;

            sb.AppendLine($"// End of file: {displayPath}");
        }

        /// <inheritdoc />
        public void AppendEndOfFolder(StringBuilder sb, string displayFolderPath)
        {
            if (sb == null) return;

            sb.AppendLine($"// End of folder: {displayFolderPath}");
        }

        /// <inheritdoc />
        public void AppendNoFilesInFolder(StringBuilder sb, string displayFolderPath)
        {
            if (sb == null) return;

            sb.AppendLine($"// No .cs files found in folder: {displayFolderPath}");
        }

        /// <inheritdoc />
        public void AppendInvalidPath(StringBuilder sb, string inputPath)
        {
            if (sb == null) return;

            sb.AppendLine($"// Invalid path: {inputPath}");
        }

        /// <inheritdoc />
        public void AppendSkippedNonCsFile(StringBuilder sb, string displayPath)
        {
            if (sb == null) return;

            sb.AppendLine($"// Skipped non-.cs file: {displayPath}");
        }

        /// <inheritdoc />
        public void AppendFileReadError(StringBuilder sb, string displayPath, string errorMessage)
        {
            if (sb == null) return;

            sb.AppendLine($"// Error reading file: {displayPath}");
            sb.AppendLine($"// Details: {errorMessage}");
        }

        /// <inheritdoc />
        public void AppendSpacing(StringBuilder sb)
        {
            if (sb == null) return;

            sb.AppendLine();
        }
    }
}
using System.Text;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Formats aggregation output text (separators, warnings, errors).
    /// </summary>
    public interface IAggregationFormatter
    {
        /// <summary>
        /// Appends a document's file content.
        /// </summary>
        void AppendFileContent(StringBuilder sb, TextDocument document);

        /// <summary>
        /// Appends an end-of-file marker.
        /// </summary>
        void AppendEndOfFile(StringBuilder sb, string displayPath);

        /// <summary>
        /// Appends an end-of-folder marker.
        /// </summary>
        void AppendEndOfFolder(StringBuilder sb, string displayFolderPath);

        /// <summary>
        /// Appends a message for folders with no .cs files.
        /// </summary>
        void AppendNoFilesInFolder(StringBuilder sb, string displayFolderPath);

        /// <summary>
        /// Appends a message for invalid input paths.
        /// </summary>
        void AppendInvalidPath(StringBuilder sb, string inputPath);

        /// <summary>
        /// Appends a message when a non-.cs file is skipped.
        /// </summary>
        void AppendSkippedNonCsFile(StringBuilder sb, string displayPath);

        /// <summary>
        /// Appends a message when a file read fails.
        /// </summary>
        void AppendFileReadError(StringBuilder sb, string displayPath, string errorMessage);

        /// <summary>
        /// Appends standard spacing between sections.
        /// </summary>
        void AppendSpacing(StringBuilder sb);
    }
}
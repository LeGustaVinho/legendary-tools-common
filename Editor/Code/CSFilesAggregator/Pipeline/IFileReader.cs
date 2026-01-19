namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Reads file content.
    /// </summary>
    public interface IFileReader
    {
        /// <summary>
        /// Reads a file as text.
        /// </summary>
        string ReadAllText(string absolutePath);
    }
}
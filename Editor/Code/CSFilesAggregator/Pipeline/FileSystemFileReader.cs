using System.IO;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Default file reader using <see cref="File.ReadAllText(string)"/>.
    /// </summary>
    public sealed class FileSystemFileReader : IFileReader
    {
        /// <inheritdoc />
        public string ReadAllText(string absolutePath)
        {
            return File.ReadAllText(absolutePath);
        }
    }
}
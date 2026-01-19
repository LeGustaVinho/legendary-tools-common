namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Kind of resolved path.
    /// </summary>
    public enum PathKind
    {
        /// <summary>
        /// Invalid or unsupported path.
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// File path.
        /// </summary>
        File = 1,

        /// <summary>
        /// Folder path.
        /// </summary>
        Folder = 2
    }
}
namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Represents a resolved path with absolute and display forms.
    /// </summary>
    public sealed class PathResolution
    {
        /// <summary>
        /// Absolute file system path.
        /// </summary>
        public string AbsolutePath { get; }

        /// <summary>
        /// Display-friendly path, preferably project-relative (e.g., Assets/...).
        /// </summary>
        public string DisplayPath { get; }

        /// <summary>
        /// Resolved kind.
        /// </summary>
        public PathKind Kind { get; }

        /// <summary>
        /// Creates a new resolution instance.
        /// </summary>
        public PathResolution(string absolutePath, string displayPath, PathKind kind)
        {
            AbsolutePath = absolutePath ?? string.Empty;
            DisplayPath = displayPath ?? string.Empty;
            Kind = kind;
        }
    }
}
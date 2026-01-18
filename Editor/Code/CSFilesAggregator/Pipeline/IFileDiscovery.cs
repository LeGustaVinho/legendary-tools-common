using System.Collections.Generic;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// Discovers C# files from a folder.
    /// </summary>
    public interface IFileDiscovery
    {
        /// <summary>
        /// Discovers .cs files under the given folder.
        /// </summary>
        IReadOnlyList<PathResolution> DiscoverCsFiles(string folderAbsolutePath, bool includeSubfolders);
    }
}

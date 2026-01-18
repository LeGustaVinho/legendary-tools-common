using System;
using System.Collections.Generic;
using System.IO;
using LegendaryTools.Editor.Code.CSFilesAggregator.Services;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline
{
    /// <summary>
    /// File-system based discovery implementation.
    /// </summary>
    public sealed class FileSystemFileDiscovery : IFileDiscovery
    {
        private readonly IPathService _pathService;

        /// <summary>
        /// Creates a new discovery instance.
        /// </summary>
        public FileSystemFileDiscovery(IPathService pathService)
        {
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        }

        /// <inheritdoc />
        public IReadOnlyList<PathResolution> DiscoverCsFiles(string folderAbsolutePath, bool includeSubfolders)
        {
            List<PathResolution> result = new List<PathResolution>();

            if (string.IsNullOrWhiteSpace(folderAbsolutePath))
            {
                return result;
            }

            if (!Directory.Exists(folderAbsolutePath))
            {
                return result;
            }

            SearchOption option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            string[] files;
            try
            {
                files = Directory.GetFiles(folderAbsolutePath, "*.cs", option);
            }
            catch
            {
                return result;
            }

            for (int i = 0; i < files.Length; i++)
            {
                string absolute = files[i];
                string display = _pathService.NormalizeToProjectDisplayPath(absolute);
                result.Add(new PathResolution(absolute, display, PathKind.File));
            }

            return result;
        }
    }
}

using LegendaryTools.Editor.Code.CSFilesAggregator.Pipeline;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// Provides Unity-project-aware path helpers.
    /// </summary>
    public interface IPathService
    {
        /// <summary>
        /// Absolute path to the project's Assets folder.
        /// </summary>
        string AssetsAbsolutePath { get; }

        /// <summary>
        /// Normalizes any input path into a project display path when possible (e.g., Assets/...).
        /// </summary>
        string NormalizeToProjectDisplayPath(string anyPath);

        /// <summary>
        /// Resolves a display path or absolute path into an absolute + display path and kind.
        /// </summary>
        PathResolution Resolve(string inputPath);
    }
}

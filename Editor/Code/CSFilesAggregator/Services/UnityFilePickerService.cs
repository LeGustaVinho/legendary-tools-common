using UnityEditor;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// Unity implementation of <see cref="IFilePickerService"/>.
    /// </summary>
    public sealed class UnityFilePickerService : IFilePickerService
    {
        /// <inheritdoc />
        public string OpenFilePanel(string title, string directory, string extension)
        {
            return EditorUtility.OpenFilePanel(title, directory, extension);
        }

        /// <inheritdoc />
        public string OpenFolderPanel(string title, string folder)
        {
            return EditorUtility.OpenFolderPanel(title, folder, string.Empty);
        }
    }
}
namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// Wraps Unity Editor file/folder selection dialogs.
    /// </summary>
    public interface IFilePickerService
    {
        /// <summary>
        /// Opens a file panel.
        /// </summary>
        string OpenFilePanel(string title, string directory, string extension);

        /// <summary>
        /// Opens a folder panel.
        /// </summary>
        string OpenFolderPanel(string title, string folder);
    }
}
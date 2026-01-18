namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// Shows modal dialogs in the Unity Editor.
    /// </summary>
    public interface IEditorDialogService
    {
        /// <summary>
        /// Shows a dialog with a single button.
        /// </summary>
        void ShowDialog(string title, string message, string ok);
    }
}

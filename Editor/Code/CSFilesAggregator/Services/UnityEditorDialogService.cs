using UnityEditor;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// Unity implementation of <see cref="IEditorDialogService"/>.
    /// </summary>
    public sealed class UnityEditorDialogService : IEditorDialogService
    {
        /// <inheritdoc />
        public void ShowDialog(string title, string message, string ok)
        {
            EditorUtility.DisplayDialog(title, message, ok);
        }
    }
}

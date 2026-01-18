using UnityEditor;

namespace LegendaryTools.Editor.Code.CSFilesAggregator.Services
{
    /// <summary>
    /// Unity implementation for clipboard access.
    /// </summary>
    public sealed class UnityClipboardService : IClipboardService
    {
        /// <inheritdoc />
        public void SetClipboardText(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text ?? string.Empty;
        }
    }
}

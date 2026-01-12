#if UNITY_EDITOR_WIN
using UnityEditor;

namespace AiClipboardPipeline.Editor
{
    internal sealed class ApplyUI
    {
        public bool Confirm(string title, string message, string ok, string cancel)
        {
            return EditorUtility.DisplayDialog(title, message, ok, cancel);
        }
    }
}
#endif
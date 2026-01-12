#if UNITY_EDITOR_WIN
using System.Collections.Generic;

namespace AiClipboardPipeline.Editor
{
    internal sealed class UndoService
    {
        public bool TryCreateUndoSession(string entryId, IReadOnlyList<string> affectedAssetPaths, out string sessionId,
            out string error)
        {
            return AICodePasteUndoManager.TryCreateUndoSession(entryId, affectedAssetPaths, out sessionId, out error);
        }

        public void BeginCompileGate(string entryId, string sessionId)
        {
            AICodePasteUndoManager.BeginCompileGate(entryId, sessionId);
        }
    }
}
#endif
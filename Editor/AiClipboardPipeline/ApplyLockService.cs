#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using UnityEditor;

namespace AiClipboardPipeline.Editor
{
    internal sealed class ApplyLockService
    {
        public enum Decision
        {
            Entered = 0,
            Blocked = 1,
            Cancelled = 2
        }

        private const double ApplyLockWindowSeconds = 2.0;

        private readonly Dictionary<string, double> _lastApplyStartByKey = new(StringComparer.Ordinal);

        public Decision TryEnter(string logicalKey, bool userInitiated, ApplyUI ui)
        {
            logicalKey ??= string.Empty;
            if (string.IsNullOrEmpty(logicalKey))
                return Decision.Entered;

            double now = EditorApplication.timeSinceStartup;

            if (!IsLocked(logicalKey, now, out double remaining))
            {
                _lastApplyStartByKey[logicalKey] = now;
                return Decision.Entered;
            }

            // If not user initiated, do not prompt; just block silently (no error state).
            if (!userInitiated)
                return Decision.Blocked;

            string msg =
                "This entry was applied very recently.\n\n" +
                $"LogicalKey: {logicalKey}\n" +
                $"Lock remaining: {remaining:0.00}s\n\n" +
                "Apply again anyway?";

            bool ok = ui.Confirm(
                "AI Code Paste - Apply Rate Limit",
                msg,
                "Apply Anyway",
                "Cancel");

            if (!ok)
                return Decision.Cancelled;

            _lastApplyStartByKey[logicalKey] = now;
            return Decision.Entered;
        }

        private bool IsLocked(string logicalKey, double now, out double remaining)
        {
            remaining = 0;

            if (string.IsNullOrEmpty(logicalKey))
                return false;

            if (!_lastApplyStartByKey.TryGetValue(logicalKey, out double last))
                return false;

            double dt = now - last;
            if (dt >= ApplyLockWindowSeconds)
                return false;

            remaining = Math.Max(0, ApplyLockWindowSeconds - dt);
            return true;
        }
    }
}
#endif
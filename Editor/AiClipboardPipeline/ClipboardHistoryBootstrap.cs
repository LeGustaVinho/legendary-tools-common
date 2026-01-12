#if UNITY_EDITOR_WIN
using System;
using UnityEditor;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    [InitializeOnLoad]
    public static class ClipboardHistoryBootstrap
    {
        private const string PrefEnabled = "AICodePasteHub.Enabled";
        private const string PrefAutoCapture = "AICodePasteHub.AutoCapture";
        private const string PrefAutoApply = "AICodePasteHub.AutoApply";
        private const string PrefMaxHistory = "AICodePasteHub.MaxHistory";
        private const string PrefFallbackFolder = "AICodePasteHub.FallbackFolder";

        static ClipboardHistoryBootstrap()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            try
            {
                ClipboardWatcher.ClipboardChanged -= OnClipboardChanged;
                ClipboardWatcher.ClipboardChanged += OnClipboardChanged;

                ApplyRuntimeSettings();
            }
            catch (Exception ex)
            {
                ClipboardHistoryStore.instance.SetLastErrorReport("[ClipboardHistoryBootstrap] Initialize failed:\n\n" +
                                                                  ex);
            }
        }

        public static void ApplyRuntimeSettings()
        {
            bool enabled = EditorPrefs.GetBool(PrefEnabled, true);

            int cap = Mathf.Clamp(EditorPrefs.GetInt(PrefMaxHistory, 200), 1, 5000);
            ClipboardHistoryStore.instance.SetCapacity(cap);

            if (enabled)
                ClipboardWatcher.Start();
            else
                ClipboardWatcher.Stop();
        }

        private static void OnClipboardChanged(string text)
        {
            bool enabled = EditorPrefs.GetBool(PrefEnabled, true);
            if (!enabled)
                return;

            bool autoCapture = EditorPrefs.GetBool(PrefAutoCapture, true);
            if (!autoCapture)
                return;

            string normalized = NormalizeClipboardText(text);

            if (string.IsNullOrWhiteSpace(normalized))
            {
                Debug.Log(
                    "[AI Clipboard Pipeline] Clipboard detected but rejected: clipboard text is empty/unavailable (possible clipboard lock).");
                return;
            }

            if (!ClipboardClassifierRegistry.TryClassify(normalized, out ClipboardClassification classification))
            {
                // Diagnose why the two known classifiers rejected.
                string csReason = string.Empty;
                _ = CSharpFileClipboardClassifier.IsValidCSharpCode(normalized, out _, out csReason);

                string hint =
                    "[AI Clipboard Pipeline] Clipboard detected but no classifier accepted the content.\n" +
                    "Details:\n" +
                    " - C# Full File: " + (string.IsNullOrEmpty(csReason) ? "not matched" : csReason) + "\n" +
                    " - Git Patch: not matched (missing diff/hunk headers)\n" +
                    "\n" +
                    "Hint: Only full C# files (class/struct/interface/enum/record) and unified git patches are captured.";

                Debug.Log(hint);
                return;
            }

            bool stored = ClipboardHistoryStore.instance.TryAddClassified(normalized, classification);
            if (!stored)
            {
                Debug.Log("[AI Clipboard Pipeline] Clipboard detected but entry was not stored (dedupe or policy).");
                return;
            }

            bool autoApply = EditorPrefs.GetBool(PrefAutoApply, true);
            if (!autoApply)
                return;

            ClipboardHistoryStore.Entry newest = ClipboardHistoryStore.instance.Entries.Count > 0
                ? ClipboardHistoryStore.instance.Entries[0]
                : null;
            if (newest == null)
                return;

            if (newest.status == (int)ClipboardHistoryStore.EntryStatus.Applied)
                return;

            AICodePasteApplier.Settings settings = new()
            {
                FallbackFolder = EditorPrefs.GetString(PrefFallbackFolder, "Assets/Scripts/Generated/")
            };

            // Auto-apply: supports both csharp_file and git_patch (if enabled).
            _ = AICodePasteApplier.TryApplyEntry(newest, settings, false);
        }

        private static string NormalizeClipboardText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string t = text.Replace("\r\n", "\n").Replace("\r", "\n");

            // Strip common Markdown fenced code blocks: ```csharp ... ```
            int fenceStart = t.IndexOf("```", StringComparison.Ordinal);
            if (fenceStart >= 0)
            {
                int fenceLangEnd = t.IndexOf('\n', fenceStart + 3);
                if (fenceLangEnd > fenceStart)
                {
                    int fenceEnd = t.IndexOf("\n```", fenceLangEnd, StringComparison.Ordinal);
                    if (fenceEnd > fenceLangEnd)
                    {
                        string inside = t.Substring(fenceLangEnd + 1, fenceEnd - (fenceLangEnd + 1));
                        t = inside;
                    }
                }
            }

            return t.Trim();
        }
    }
}
#endif
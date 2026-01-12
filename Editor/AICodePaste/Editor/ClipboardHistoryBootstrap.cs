#if UNITY_EDITOR_WIN
using System;
using System.Text;
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

        // Anti-spam for reject logs.
        private static string s_LastRejectKey;
        private static double s_LastRejectTime;

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
                ClipboardHistoryStore.instance.SetLastErrorReport("[ClipboardHistoryBootstrap] Initialize failed:\n\n" + ex);
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
            // We only log "not stored" cases when capture is enabled, otherwise it's intentional.
            bool enabled = EditorPrefs.GetBool(PrefEnabled, true);
            if (!enabled)
                return;

            bool autoCapture = EditorPrefs.GetBool(PrefAutoCapture, true);
            if (!autoCapture)
                return;

            if (string.IsNullOrEmpty(text))
            {
                LogRejectOnce("empty", "Clipboard detected but text is empty (nothing to capture).", text);
                return;
            }

            if (!ClipboardClassifierRegistry.TryClassify(text, out ClipboardClassification classification) || classification == null)
            {
                string hint = BuildClassificationHint(text);
                LogRejectOnce("no_classifier", "Clipboard detected but no classifier accepted the content." + hint, text);
                return;
            }

            bool stored = ClipboardHistoryStore.instance.TryAddClassified(text, classification, out string reason);
            if (!stored)
            {
                string msg =
                    "Clipboard detected but entry was NOT stored.\n" +
                    $"Reason: {reason}\n" +
                    $"TypeId: {classification.TypeId}\n" +
                    $"LogicalKey: {classification.LogicalKey}\n" +
                    $"Length: {text.Length}\n" +
                    $"Preview: {MakePreview(text, 120)}";

                LogRejectOnce($"store_reject:{classification.TypeId}:{classification.LogicalKey}", msg, text);
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

            if (!string.Equals(newest.typeId, "csharp_file", StringComparison.Ordinal) &&
                !string.Equals(newest.typeId, "git_patch", StringComparison.Ordinal))
                return;

            AICodePasteApplier.Settings settings = new()
            {
                FallbackFolder = EditorPrefs.GetString(PrefFallbackFolder, "Assets/Scripts/Generated/")
            };

            AICodePasteApplier.TryApplyEntry(newest, settings, userInitiated: false);
        }

        private static void LogRejectOnce(string key, string message, string clipboardText)
        {
            double now = EditorApplication.timeSinceStartup;

            // Build a key that changes with content length + prefix, to avoid log spam.
            string stableKey = key + "|" + clipboardText.Length + "|" + MakePreview(clipboardText, 32);

            // Skip if same rejection repeats too quickly (the watcher does retries).
            if (string.Equals(s_LastRejectKey, stableKey, StringComparison.Ordinal) && (now - s_LastRejectTime) < 0.6)
                return;

            s_LastRejectKey = stableKey;
            s_LastRejectTime = now;

            Debug.LogWarning("[AI Clipboard Pipeline] " + message);
        }

        private static string MakePreview(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
                return "(empty)";

            string t = text.Replace("\r\n", "\n").Replace("\r", "\n");
            t = t.Trim();

            if (t.Length <= maxChars)
                return t;

            return t.Substring(0, maxChars) + "…";
        }

        private static string BuildClassificationHint(string text)
        {
            // Provide a short actionable hint for the two built-in classifiers.
            // Keep it lightweight: no heavy regex scans here.
            string t = text ?? string.Empty;

            bool looksLikePatch = t.IndexOf("diff --git", StringComparison.Ordinal) >= 0 ||
                                  t.IndexOf("\n@@", StringComparison.Ordinal) >= 0;

            bool looksLikeCSharp = t.IndexOf("class ", StringComparison.Ordinal) >= 0 ||
                                   t.IndexOf("struct ", StringComparison.Ordinal) >= 0 ||
                                   t.IndexOf("interface ", StringComparison.Ordinal) >= 0 ||
                                   t.IndexOf("enum ", StringComparison.Ordinal) >= 0;

            var sb = new StringBuilder(128);

            if (looksLikePatch)
            {
                sb.Append("\nHint: If this is a git patch, it must include at least one hunk line like \"@@ ... @@\".");
                sb.Append("\nHint: Only patches that touch \"Assets/\" are allowed (other folders are blocked).");
            }

            if (looksLikeCSharp)
            {
                sb.Append("\nHint: If this is a full C# file, it must contain a type declaration and balanced braces.");
            }

            if (sb.Length == 0)
            {
                sb.Append("\nHint: Content may be plain text; only full C# files and unified git patches are captured.");
            }

            return sb.ToString();
        }
    }
}
#endif

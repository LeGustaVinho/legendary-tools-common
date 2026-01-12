#if UNITY_EDITOR_WIN
using System;

namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Orchestrates applying classified clipboard entries into Unity project files.
    /// Responsibilities:
    /// - Routes to specific handlers by TypeId.
    /// - Applies rate-limit lock policy (with optional UI confirmation).
    /// - Centralizes failure reporting and store updates.
    /// </summary>
    public static class AICodePasteApplier
    {
        public sealed class Settings
        {
            public string FallbackFolder;
        }

        private static readonly ApplyLockService s_Lock = new();
        private static readonly ApplyUI s_UI = new();
        private static readonly ApplyReporter s_Report = new();
        private static readonly UnityAssetService s_Assets = new();
        private static readonly UndoService s_Undo = new();
        private static readonly GitService s_Git = new();

        private static readonly PathSafety s_PathSafety = new();
        private static readonly TextNormalization s_Text = new();
        private static readonly PatchAssetExtractor s_PatchExtractor = new();
        private static readonly TargetPathResolver s_TargetResolver = new();

        private static readonly IClipboardApplyHandler[] s_Handlers =
        {
            new CSharpFileApplyHandler(),
            new GitPatchApplyHandler()
        };

        public static bool TryApplyEntryById(string entryId, Settings settings)
        {
            ClipboardHistoryStore.Entry entry = ClipboardHistoryStore.instance.GetById(entryId);
            if (entry == null)
            {
                s_Report.ReportFailure(
                    "Apply failed: entry not found.",
                    entryId,
                    false);

                return false;
            }

            return TryApplyEntry(entry, settings, true);
        }

        public static bool TryApplyEntry(ClipboardHistoryStore.Entry entry, Settings settings,
            bool userInitiated = false)
        {
            if (entry == null)
            {
                s_Report.ReportFailure(
                    "Apply failed: entry is null.",
                    null,
                    false);

                return false;
            }

            string text = entry.text ?? string.Empty;

            IClipboardApplyHandler handler = FindHandler(entry.typeId);
            if (handler == null)
            {
                s_Report.ReportFailure(
                    $"Apply not implemented for type '{entry.typeId}'.",
                    entry.id,
                    true);

                return false;
            }

            string logicalKey = string.IsNullOrEmpty(entry.logicalKey) ? $"{entry.typeId}:unknown" : entry.logicalKey;

            // Rate limit lock: cancelled/blocked should be silent and should not mark entry error.
            ApplyLockService.Decision lockDecision = s_Lock.TryEnter(logicalKey, userInitiated, s_UI);
            if (lockDecision != ApplyLockService.Decision.Entered)
                return false;

            ApplyServices services = new(
                s_UI,
                s_Report,
                s_Assets,
                s_Undo,
                s_Git,
                s_PathSafety,
                s_Text,
                s_PatchExtractor,
                s_TargetResolver);

            ApplyContext ctx = new(entry, text, settings, userInitiated, services);

            try
            {
                ApplyResult result = handler.Execute(ctx);
                if (!result.Success)
                {
                    if (result.ShouldLog)
                        s_Report.ReportFailure(
                            string.IsNullOrEmpty(result.ErrorReport) ? "Apply failed." : result.ErrorReport,
                            entry.id,
                            result.SetEntryError);

                    return false;
                }

                ClipboardHistoryStore.instance.UpdateEntryResult(
                    entry.id,
                    ClipboardHistoryStore.EntryStatus.Applied,
                    result.AppliedAssetPathNote ?? string.Empty,
                    string.Empty);

                return true;
            }
            catch (Exception ex)
            {
                string report =
                    "Apply failed.\n\n" +
                    $"Entry: {entry.id}\n" +
                    $"Type: {entry.typeId}\n" +
                    $"LogicalKey: {entry.logicalKey}\n\n" +
                    ex;

                s_Report.ReportFailure(report, entry.id, true);
                return false;
            }
        }

        private static IClipboardApplyHandler FindHandler(string typeId)
        {
            if (string.IsNullOrEmpty(typeId))
                return null;

            for (int i = 0; i < s_Handlers.Length; i++)
            {
                if (string.Equals(s_Handlers[i].TypeId, typeId, StringComparison.Ordinal))
                    return s_Handlers[i];
            }

            return null;
        }
    }
}
#endif
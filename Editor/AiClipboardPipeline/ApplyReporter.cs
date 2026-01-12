#if UNITY_EDITOR_WIN
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    internal sealed class ApplyReporter
    {
        public void ReportFailure(string report, string entryId, bool setEntryError)
        {
            if (string.IsNullOrEmpty(report))
                report = "Unknown failure.";

            // Persist for internal consumers, but canonical surface is Console.
            ClipboardHistoryStore.instance.SetLastErrorReport(report);

            Debug.LogError(report);

            if (!string.IsNullOrEmpty(entryId) && setEntryError)
                ClipboardHistoryStore.instance.UpdateEntryResult(
                    entryId,
                    ClipboardHistoryStore.EntryStatus.Error,
                    string.Empty,
                    report);
        }
    }
}
#endif
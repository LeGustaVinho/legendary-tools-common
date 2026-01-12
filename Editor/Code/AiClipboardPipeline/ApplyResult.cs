#if UNITY_EDITOR_WIN
namespace AiClipboardPipeline.Editor
{
    internal readonly struct ApplyResult
    {
        public bool Success { get; }
        public bool ShouldLog { get; }
        public bool SetEntryError { get; }
        public string AppliedAssetPathNote { get; }
        public string ErrorReport { get; }

        private ApplyResult(bool success, bool shouldLog, bool setEntryError, string appliedAssetPathNote,
            string errorReport)
        {
            Success = success;
            ShouldLog = shouldLog;
            SetEntryError = setEntryError;
            AppliedAssetPathNote = appliedAssetPathNote ?? string.Empty;
            ErrorReport = errorReport ?? string.Empty;
        }

        public static ApplyResult Ok(string appliedAssetPathNote)
        {
            return new ApplyResult(
                true,
                false,
                false,
                appliedAssetPathNote,
                string.Empty);
        }

        public static ApplyResult Fail(string errorReport)
        {
            return new ApplyResult(
                false,
                true,
                true,
                string.Empty,
                errorReport);
        }

        public static ApplyResult Blocked(string report, bool shouldLog = false, bool setEntryError = false)
        {
            return new ApplyResult(
                false,
                shouldLog,
                setEntryError,
                string.Empty,
                report);
        }

        public static ApplyResult Cancelled()
        {
            return new ApplyResult(
                false,
                false,
                false,
                string.Empty,
                string.Empty);
        }
    }
}
#endif
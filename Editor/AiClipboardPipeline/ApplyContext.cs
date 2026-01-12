#if UNITY_EDITOR_WIN
namespace AiClipboardPipeline.Editor
{
    internal sealed class ApplyContext
    {
        public ClipboardHistoryStore.Entry Entry { get; }
        public string Text { get; }
        public AICodePasteApplier.Settings Settings { get; }
        public bool UserInitiated { get; }
        public ApplyServices Services { get; }

        public ApplyContext(
            ClipboardHistoryStore.Entry entry,
            string text,
            AICodePasteApplier.Settings settings,
            bool userInitiated,
            ApplyServices services)
        {
            Entry = entry;
            Text = text ?? string.Empty;
            Settings = settings;
            UserInitiated = userInitiated;
            Services = services;
        }
    }
}
#endif
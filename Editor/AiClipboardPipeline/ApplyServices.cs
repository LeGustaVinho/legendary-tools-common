#if UNITY_EDITOR_WIN
namespace AiClipboardPipeline.Editor
{
    internal sealed class ApplyServices
    {
        public ApplyUI UI { get; }
        public ApplyReporter Reporter { get; }
        public UnityAssetService Assets { get; }
        public UndoService Undo { get; }
        public GitService Git { get; }
        public PathSafety PathSafety { get; }
        public TextNormalization Text { get; }
        public PatchAssetExtractor PatchExtractor { get; }
        public TargetPathResolver TargetResolver { get; }

        public ApplyServices(
            ApplyUI ui,
            ApplyReporter reporter,
            UnityAssetService assets,
            UndoService undo,
            GitService git,
            PathSafety pathSafety,
            TextNormalization text,
            PatchAssetExtractor patchExtractor,
            TargetPathResolver targetResolver)
        {
            UI = ui;
            Reporter = reporter;
            Assets = assets;
            Undo = undo;
            Git = git;
            PathSafety = pathSafety;
            Text = text;
            PatchExtractor = patchExtractor;
            TargetResolver = targetResolver;
        }
    }
}
#endif
#if UNITY_EDITOR_WIN
namespace AiClipboardPipeline.Editor
{
    internal interface IClipboardApplyHandler
    {
        string TypeId { get; }
        ApplyResult Execute(ApplyContext ctx);
    }
}
#endif
#if UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Text;

namespace AiClipboardPipeline.Editor
{
    internal sealed class CSharpFileApplyHandler : IClipboardApplyHandler
    {
        public string TypeId => "csharp_file";

        public ApplyResult Execute(ApplyContext ctx)
        {
            string text = ctx.Text ?? string.Empty;

            if (!CSharpFileClipboardClassifier.IsValidCSharpCode(text, out _, out string reason))
            {
                string report =
                    "Apply blocked.\n\n" +
                    "Reason: Clipboard text is not a valid C# full file (heuristic validation failed).\n" +
                    "Details:\n" +
                    (string.IsNullOrEmpty(reason) ? " - (no details)\n" : $" - {reason}\n") +
                    "\nRequirements:\n" +
                    " - Must contain class/struct/interface/enum/record + identifier\n" +
                    " - Must contain '{' and '}'\n" +
                    " - Braces must be balanced (ignoring comments and strings)\n";

                return ApplyResult.Fail(report);
            }

            string fallbackFolder = ctx.Settings?.FallbackFolder;
            string assetPath = ctx.Services.TargetResolver.ResolveTargetAssetPath(text, fallbackFolder, out _);

            string absPath;
            try
            {
                absPath = ctx.Services.PathSafety.ToAbsolutePathStrict(assetPath);
            }
            catch (Exception ex)
            {
                return ApplyResult.Fail("Apply blocked: invalid target asset path.\n\n" + ex);
            }

            if (!ctx.Services.Undo.TryCreateUndoSession(ctx.Entry.id, new[] { assetPath }, out string sessionId,
                    out string undoErr))
                return ApplyResult.Fail("Apply blocked: failed to create undo session.\n\n" + undoErr);

            try
            {
                string dir = Path.GetDirectoryName(absPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string normalized = ctx.Services.Text.NormalizeToLF(text);
                normalized = ctx.Services.Text.StripFileHeaderIfPresent(normalized);
                normalized = ctx.Services.Text.EnsureTrailingNewline(normalized);

                File.WriteAllText(absPath, normalized, new UTF8Encoding(false));

                ctx.Services.Undo.BeginCompileGate(ctx.Entry.id, sessionId);

                ctx.Services.Assets.ImportAsset(assetPath);
                ctx.Services.Assets.Refresh();

                return ApplyResult.Ok(assetPath);
            }
            catch (Exception ex)
            {
                return ApplyResult.Fail(
                    "Apply failed.\n\n" +
                    $"Entry: {ctx.Entry.id}\n" +
                    $"Type: {ctx.Entry.typeId}\n" +
                    $"LogicalKey: {ctx.Entry.logicalKey}\n\n" +
                    ex);
            }
        }
    }
}
#endif
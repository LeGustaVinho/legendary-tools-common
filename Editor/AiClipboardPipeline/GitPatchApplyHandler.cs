#if UNITY_EDITOR_WIN
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace AiClipboardPipeline.Editor
{
    internal sealed class GitPatchApplyHandler : IClipboardApplyHandler
    {
        public string TypeId => "git_patch";

        public ApplyResult Execute(ApplyContext ctx)
        {
            string patchText = ctx.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(patchText))
                return ApplyResult.Fail("Apply blocked: patch text is empty.");

            if (!ctx.Services.PatchExtractor.TryExtractAffectedAssetsFromPatch(
                    patchText,
                    ctx.Services.PathSafety,
                    out List<string> assetPaths,
                    out string validationError))
            {
                string report =
                    "Apply blocked.\n\n" +
                    "Reason: Patch failed safety validation.\n\n" +
                    validationError;

                // Validation failure should be logged + marked as error.
                return ApplyResult.Fail(report);
            }

            // Confirm multi-file patches (only when user initiated).
            if (assetPaths.Count > 1)
            {
                if (!ctx.UserInitiated)
                    // Auto-apply should not mark error; just skip silently.
                    return ApplyResult.Blocked(
                        "Patch skipped: multi-file patch is not allowed in auto-apply mode.",
                        false,
                        false);

                string msg =
                    "This patch will modify multiple files:\n\n" +
                    string.Join("\n", assetPaths) +
                    "\n\nApply now?";

                bool ok = ctx.Services.UI.Confirm(
                    "AI Code Paste - Apply Patch",
                    msg,
                    "Apply",
                    "Cancel");

                if (!ok)
                    return ApplyResult.Cancelled();
            }

            if (!ctx.Services.Undo.TryCreateUndoSession(ctx.Entry.id, assetPaths, out string sessionId,
                    out string undoErr))
                return ApplyResult.Fail("Apply blocked: failed to create undo session.\n\n" + undoErr);

            string projectRoot = ProjectPaths.GetProjectRoot();

            try
            {
                // Write patch to temp file.
                string tempDir = ProjectPaths.GetTempPatchDirectory(projectRoot);
                Directory.CreateDirectory(tempDir);

                string tempPatchPath = Path.Combine(tempDir, "patch_" + ctx.Entry.id + ".diff");

                string normalized = ctx.Services.Text.NormalizeToLF(patchText);
                normalized = ctx.Services.Text.EnsureTrailingNewline(normalized);

                File.WriteAllText(tempPatchPath, normalized, new UTF8Encoding(false));

                // git apply --check
                GitResult check = ctx.Services.Git.Run(projectRoot, $"apply --check \"{tempPatchPath}\"");
                if (check.ExitCode != 0)
                {
                    string report =
                        "Patch apply check failed.\n\n" +
                        "Command: git apply --check\n\n" +
                        $"ExitCode: {check.ExitCode}\n\n" +
                        "STDOUT:\n" + check.StdOut + "\n\n" +
                        "STDERR:\n" + check.StdErr;

                    // Must be logged to Console, and should mark entry as error.
                    return ApplyResult.Fail(report);
                }

                // git apply
                GitResult apply = ctx.Services.Git.Run(projectRoot, $"apply --whitespace=nowarn \"{tempPatchPath}\"");
                if (apply.ExitCode != 0)
                {
                    string report =
                        "Patch apply failed.\n\n" +
                        "Command: git apply\n\n" +
                        $"ExitCode: {apply.ExitCode}\n\n" +
                        "STDOUT:\n" + apply.StdOut + "\n\n" +
                        "STDERR:\n" + apply.StdErr;

                    // Must be logged to Console, and should mark entry as error.
                    return ApplyResult.Fail(report);
                }

                // Begin compile gate now; compilation will occur after imports.
                ctx.Services.Undo.BeginCompileGate(ctx.Entry.id, sessionId);

                // Import touched assets.
                ctx.Services.Assets.ImportManyIfExists(assetPaths);
                ctx.Services.Assets.Refresh();

                string appliedNote = assetPaths.Count == 1 ? assetPaths[0] : $"(patch) {assetPaths.Count} files";
                return ApplyResult.Ok(appliedNote);
            }
            catch (System.Exception ex)
            {
                string report =
                    "Patch apply failed.\n\n" +
                    $"Entry: {ctx.Entry.id}\n" +
                    $"Type: {ctx.Entry.typeId}\n" +
                    $"LogicalKey: {ctx.Entry.logicalKey}\n\n" +
                    ex;

                return ApplyResult.Fail(report);
            }
        }
    }
}
#endif
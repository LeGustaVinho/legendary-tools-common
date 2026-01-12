#if UNITY_EDITOR_WIN
using System;
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

                return ApplyResult.Fail(report);
            }

            // Confirm multi-file patches (only when user initiated).
            if (assetPaths.Count > 1)
            {
                if (!ctx.UserInitiated)
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

            string tempDir = string.Empty;
            string tempPatchPath = string.Empty;

            // Delete temp patch only on success; keep on failure to help debugging.
            bool deleteTempPatchOnExit = false;

            try
            {
                tempDir = ProjectPaths.GetTempPatchDirectory(projectRoot);
                Directory.CreateDirectory(tempDir);

                tempPatchPath = Path.Combine(tempDir, "patch_" + ctx.Entry.id + ".diff");

                string normalized = ctx.Services.Text.NormalizeToLF(patchText);
                normalized = ctx.Services.Text.EnsureTrailingNewline(normalized);

                File.WriteAllText(tempPatchPath, normalized, new UTF8Encoding(false));

                // git apply --check
                GitResult check = ctx.Services.Git.Run(projectRoot, $"apply --check \"{tempPatchPath}\"");
                if (check.ExitCode != 0)
                {
                    // Fallback to manual apply.
                    if (ManualUnifiedDiffApplier.TryApply(projectRoot, normalized, out string manualError,
                            out List<string> manualTouchedFiles))
                    {
                        deleteTempPatchOnExit = true;

                        ctx.Services.Undo.BeginCompileGate(ctx.Entry.id, sessionId);

                        // Prefer the extractor list for import/refresh, but manual may include create/delete.
                        ctx.Services.Assets.ImportManyIfExists(assetPaths);
                        ctx.Services.Assets.Refresh();

                        string appliedNote =
                            assetPaths.Count == 1 ? assetPaths[0] : $"(patch) {assetPaths.Count} files";
                        return ApplyResult.Ok(appliedNote);
                    }

                    string report =
                        "Patch apply check failed.\n\n" +
                        "Command: git apply --check\n\n" +
                        $"ExitCode: {check.ExitCode}\n\n" +
                        "STDOUT:\n" + check.StdOut + "\n\n" +
                        "STDERR:\n" + check.StdErr + "\n\n" +
                        "Manual apply error:\n" + manualError + "\n\n" +
                        "TempPatch:\n" + tempPatchPath;

                    deleteTempPatchOnExit = false;
                    return ApplyResult.Fail(report);
                }

                // git apply
                GitResult apply = ctx.Services.Git.Run(projectRoot, $"apply --whitespace=nowarn \"{tempPatchPath}\"");
                if (apply.ExitCode != 0)
                {
                    // Fallback to manual apply.
                    if (ManualUnifiedDiffApplier.TryApply(projectRoot, normalized, out string manualError,
                            out List<string> manualTouchedFiles))
                    {
                        deleteTempPatchOnExit = true;

                        ctx.Services.Undo.BeginCompileGate(ctx.Entry.id, sessionId);

                        ctx.Services.Assets.ImportManyIfExists(assetPaths);
                        ctx.Services.Assets.Refresh();

                        string appliedNote =
                            assetPaths.Count == 1 ? assetPaths[0] : $"(patch) {assetPaths.Count} files";
                        return ApplyResult.Ok(appliedNote);
                    }

                    string report =
                        "Patch apply failed.\n\n" +
                        "Command: git apply\n\n" +
                        $"ExitCode: {apply.ExitCode}\n\n" +
                        "STDOUT:\n" + apply.StdOut + "\n\n" +
                        "STDERR:\n" + apply.StdErr + "\n\n" +
                        "Manual apply error:\n" + manualError + "\n\n" +
                        "TempPatch:\n" + tempPatchPath;

                    deleteTempPatchOnExit = false;
                    return ApplyResult.Fail(report);
                }

                deleteTempPatchOnExit = true;

                ctx.Services.Undo.BeginCompileGate(ctx.Entry.id, sessionId);

                ctx.Services.Assets.ImportManyIfExists(assetPaths);
                ctx.Services.Assets.Refresh();

                string note = assetPaths.Count == 1 ? assetPaths[0] : $"(patch) {assetPaths.Count} files";
                return ApplyResult.Ok(note);
            }
            catch (Exception ex)
            {
                string report =
                    "Patch apply failed.\n\n" +
                    $"Entry: {ctx.Entry.id}\n" +
                    $"Type: {ctx.Entry.typeId}\n" +
                    $"LogicalKey: {ctx.Entry.logicalKey}\n\n" +
                    (string.IsNullOrEmpty(tempPatchPath) ? string.Empty : "TempPatch:\n" + tempPatchPath + "\n\n") +
                    ex;

                deleteTempPatchOnExit = false;
                return ApplyResult.Fail(report);
            }
            finally
            {
                if (deleteTempPatchOnExit && !string.IsNullOrEmpty(tempPatchPath))
                    try
                    {
                        if (File.Exists(tempPatchPath))
                            File.Delete(tempPatchPath);

                        if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
                        {
                            string[] files = Directory.GetFiles(tempDir);
                            string[] dirs = Directory.GetDirectories(tempDir);
                            if ((files == null || files.Length == 0) && (dirs == null || dirs.Length == 0))
                                Directory.Delete(tempDir, false);
                        }
                    }
                    catch
                    {
                        // Ignore cleanup errors.
                    }
            }
        }
    }
}
#endif
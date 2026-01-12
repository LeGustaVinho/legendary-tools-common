#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Applies classified clipboard entries into Unity project files.
    /// Safety:
    /// - Supports "csharp_file" and "git_patch".
    /// - Validates full C# file heuristically when applying "csharp_file".
    /// - Applies "git_patch" using "git apply" with a "git apply --check" validation step.
    /// - Prevents modifying outside Assets/ (path traversal and out-of-project protection).
    /// - Rate limits repeated applies for the same logicalKey (lock/queue/confirm).
    /// </summary>
    public static class AICodePasteApplier
    {
        private const double ApplyLockWindowSeconds = 2.0;
        private const int MaxQueuedAttempts = 12;

        private static readonly Dictionary<string, double> s_LastApplyStartByKey = new(StringComparer.Ordinal);
        private static readonly Queue<QueuedApply> s_QueuedApplies = new();
        private static bool s_UpdateHooked;

        private static readonly Regex TypeRegex =
            new Regex(@"\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);

        private static readonly Regex NamespaceRegex =
            new Regex(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_\.]*)\s*[{;]", RegexOptions.Compiled);

        private static readonly Regex FileHeaderRegex =
            new Regex(@"^\s*//\s*File\s*:\s*(.+?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GitDiffHeaderRegex =
            new Regex(@"^diff --git a\/(.+?) b\/(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex GitPlusPlusPlusRegex =
            new Regex(@"^\+\+\+\s+(b\/|\/dev\/null)(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

        private struct QueuedApply
        {
            public string EntryId;
            public Settings Settings;
            public string LogicalKey;
            public double EnqueuedAt;
            public int Attempts;
        }

        public sealed class Settings
        {
            public string FallbackFolder;
        }

        public static bool TryApplyEntryById(string entryId, Settings settings)
        {
            var store = ClipboardHistoryStore.instance;
            var entry = store.GetById(entryId);
            if (entry == null)
            {
                ReportFailure("Apply failed: entry not found.", entryId, setEntryError: false);
                return false;
            }

            return TryApplyEntry(entry, settings, userInitiated: true);
        }

        public static bool TryApplyEntry(ClipboardHistoryStore.Entry entry, Settings settings, bool userInitiated = false)
        {
            if (entry == null)
            {
                ReportFailure("Apply failed: entry is null.", null, setEntryError: false);
                return false;
            }

            string text = entry.text ?? string.Empty;

            if (string.Equals(entry.typeId, "csharp_file", StringComparison.Ordinal))
                return TryApplyCSharpFile(entry, text, settings, userInitiated);

            if (string.Equals(entry.typeId, "git_patch", StringComparison.Ordinal))
                return TryApplyGitPatch(entry, text, userInitiated);

            string msg = $"Apply not implemented for type '{entry.typeId}'.";
            ReportFailure(msg, entry.id, setEntryError: true);
            return false;
        }

        // ---------------------------------------------------------------------
        // Apply: C# full file
        // ---------------------------------------------------------------------

        private static bool TryApplyCSharpFile(ClipboardHistoryStore.Entry entry, string text, Settings settings, bool userInitiated)
        {
            if (!CSharpFileClipboardClassifier.IsValidCSharpCode(text, out _))
            {
                string report =
                    "Apply blocked.\n\n" +
                    "Reason: Clipboard text is not a valid C# full file (heuristic validation failed).\n" +
                    "Requirements:\n" +
                    " - Must contain class/struct/interface/enum + identifier\n" +
                    " - Must contain '{' and '}'\n" +
                    " - Braces must be balanced (ignoring comments and strings)\n";

                ReportFailure(report, entry.id, setEntryError: true);
                return false;
            }

            string logicalKey = entry.logicalKey;
            if (string.IsNullOrEmpty(logicalKey))
                logicalKey = ExtractPrimaryTypeName(text);

            if (!TryEnterApplyLock(logicalKey, userInitiated, entry.id, settings))
                return false;

            try
            {
                string assetPath = ResolveTargetAssetPath(text, settings?.FallbackFolder, out _);

                string absPath = ToAbsolutePathStrict(assetPath);
                string dir = Path.GetDirectoryName(absPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string normalized = text ?? string.Empty;
                if (!normalized.EndsWith("\n", StringComparison.Ordinal))
                    normalized += "\n";

                File.WriteAllText(absPath, normalized, new UTF8Encoding(false));

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();

                ClipboardHistoryStore.instance.UpdateEntryResult(
                    entry.id,
                    ClipboardHistoryStore.EntryStatus.Applied,
                    appliedAssetPath: assetPath,
                    errorReport: string.Empty);

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

                ReportFailure(report, entry.id, setEntryError: true);
                return false;
            }
        }

        // ---------------------------------------------------------------------
        // Apply: Git patch
        // ---------------------------------------------------------------------

        private static bool TryApplyGitPatch(ClipboardHistoryStore.Entry entry, string patchText, bool userInitiated)
        {
            if (string.IsNullOrWhiteSpace(patchText))
            {
                ReportFailure("Apply blocked: patch text is empty.", entry.id, setEntryError: true);
                return false;
            }

            if (!TryExtractAffectedAssetsFromPatch(patchText, out List<string> assetPaths, out string validationError))
            {
                string report =
                    "Apply blocked.\n\n" +
                    "Reason: Patch failed safety validation.\n\n" +
                    validationError;

                ReportFailure(report, entry.id, setEntryError: true);
                return false;
            }

            string logicalKey = string.IsNullOrEmpty(entry.logicalKey) ? "patch:unknown" : entry.logicalKey;

            if (!TryEnterApplyLock(logicalKey, userInitiated, entry.id, settings: null))
                return false;

            // Auto-apply is enabled: confirm multi-file patches (both auto + manual).
            if (assetPaths.Count > 1)
            {
                string msg =
                    "This patch will modify multiple files:\n\n" +
                    string.Join("\n", assetPaths) +
                    "\n\nApply now?";

                bool ok = EditorUtility.DisplayDialog("AI Code Paste - Apply Patch", msg, "Apply", "Cancel");
                if (!ok)
                    return false;
            }

            try
            {
                string projectRoot = GetProjectRoot();

                string tempDir = Path.Combine(projectRoot, "Library", "AICodePasteTemp");
                Directory.CreateDirectory(tempDir);

                string tempPatchPath = Path.Combine(tempDir, "patch_" + entry.id + ".diff");

                string normalized = (patchText ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
                if (!normalized.EndsWith("\n", StringComparison.Ordinal))
                    normalized += "\n";

                File.WriteAllText(tempPatchPath, normalized, new UTF8Encoding(false));

                GitResult check = RunGit(projectRoot, $"apply --check \"{tempPatchPath}\"");
                if (check.ExitCode != 0)
                {
                    string report =
                        "Patch apply check failed.\n\n" +
                        "Command: git apply --check\n\n" +
                        $"ExitCode: {check.ExitCode}\n\n" +
                        "STDOUT:\n" + check.StdOut + "\n\n" +
                        "STDERR:\n" + check.StdErr;

                    ReportFailure(report, entry.id, setEntryError: true);
                    return false;
                }

                GitResult apply = RunGit(projectRoot, $"apply --whitespace=nowarn \"{tempPatchPath}\"");
                if (apply.ExitCode != 0)
                {
                    string report =
                        "Patch apply failed.\n\n" +
                        "Command: git apply\n\n" +
                        $"ExitCode: {apply.ExitCode}\n\n" +
                        "STDOUT:\n" + apply.StdOut + "\n\n" +
                        "STDERR:\n" + apply.StdErr;

                    ReportFailure(report, entry.id, setEntryError: true);
                    return false;
                }

                // Import modified assets (defense-in-depth: strict absolute validation).
                for (int i = 0; i < assetPaths.Count; i++)
                {
                    string ap = assetPaths[i];
                    _ = ToAbsolutePathStrict(ap);

                    if (File.Exists(AssetPathToAbsolute(ap)))
                        AssetDatabase.ImportAsset(ap, ImportAssetOptions.ForceUpdate);
                }

                AssetDatabase.Refresh();

                string appliedNote = assetPaths.Count == 1 ? assetPaths[0] : $"(patch) {assetPaths.Count} files";
                ClipboardHistoryStore.instance.UpdateEntryResult(
                    entry.id,
                    ClipboardHistoryStore.EntryStatus.Applied,
                    appliedAssetPath: appliedNote,
                    errorReport: string.Empty);

                return true;
            }
            catch (Exception ex)
            {
                string report =
                    "Patch apply failed.\n\n" +
                    $"Entry: {entry.id}\n" +
                    $"Type: {entry.typeId}\n" +
                    $"LogicalKey: {entry.logicalKey}\n\n" +
                    ex;

                ReportFailure(report, entry.id, setEntryError: true);
                return false;
            }
        }

        private static bool TryExtractAffectedAssetsFromPatch(string patchText, out List<string> assetPaths, out string error)
        {
            assetPaths = new List<string>(4);
            error = string.Empty;

            MatchCollection diffs = GitDiffHeaderRegex.Matches(patchText);
            for (int i = 0; i < diffs.Count; i++)
            {
                string bPath = diffs[i].Groups[2].Value.Trim();
                if (string.IsNullOrEmpty(bPath))
                    continue;

                if (string.Equals(bPath, "/dev/null", StringComparison.OrdinalIgnoreCase))
                    continue;

                AddValidatedAssetPath(bPath, assetPaths, ref error);
                if (!string.IsNullOrEmpty(error))
                    return false;
            }

            if (assetPaths.Count == 0)
            {
                MatchCollection plus = GitPlusPlusPlusRegex.Matches(patchText);
                for (int i = 0; i < plus.Count; i++)
                {
                    string kind = (plus[i].Groups[1].Value ?? string.Empty).Trim();
                    string rest = (plus[i].Groups[2].Value ?? string.Empty).Trim();

                    if (string.Equals(kind, "/dev/null", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string path = rest.TrimStart(' ', '\t').Trim();
                    if (string.IsNullOrEmpty(path))
                        continue;

                    AddValidatedAssetPath(path, assetPaths, ref error);
                    if (!string.IsNullOrEmpty(error))
                        return false;
                }
            }

            if (assetPaths.Count == 0)
            {
                error = "Patch does not contain any recognizable file headers (diff --git / +++ b/...).";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            int write = 0;
            for (int i = 0; i < assetPaths.Count; i++)
            {
                string p = assetPaths[i];
                if (seen.Add(p))
                    assetPaths[write++] = p;
            }

            if (write != assetPaths.Count)
                assetPaths.RemoveRange(write, assetPaths.Count - write);

            return true;
        }

        private static void AddValidatedAssetPath(string rawPath, List<string> list, ref string error)
        {
            if (string.IsNullOrEmpty(rawPath))
                return;

            string p = rawPath.Replace("\\", "/").Trim();

            if (p.StartsWith("/", StringComparison.Ordinal) || Regex.IsMatch(p, @"^[A-Za-z]:/"))
            {
                error = "Absolute paths are not allowed in patches: " + rawPath;
                return;
            }

            if (!p.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "Patch touches a non-Assets path (blocked): " + p;
                return;
            }

            string rel = p.Substring("Assets/".Length);
            if (string.IsNullOrEmpty(rel))
            {
                error = "Patch targets Assets root (blocked): " + p;
                return;
            }

            if (ContainsPathTraversal(rel))
            {
                error = "Path traversal is not allowed in patches: " + p;
                return;
            }

            _ = ToAbsolutePathStrict(p);

            list.Add(p);
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            string assetsAbs = Application.dataPath.Replace("\\", "/");
            string rel = assetPath.Substring("Assets/".Length);
            return Path.Combine(assetsAbs, rel).Replace("\\", "/");
        }

        private struct GitResult
        {
            public int ExitCode;
            public string StdOut;
            public string StdErr;
        }

        private static GitResult RunGit(string workingDirectory, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = workingDirectory,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = new Process { StartInfo = psi };

            try
            {
                p.Start();

                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();

                p.WaitForExit();

                return new GitResult
                {
                    ExitCode = p.ExitCode,
                    StdOut = stdout ?? string.Empty,
                    StdErr = stderr ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                return new GitResult
                {
                    ExitCode = -1,
                    StdOut = string.Empty,
                    StdErr = "Failed to start git process.\n\n" + ex
                };
            }
        }

        private static string GetProjectRoot()
        {
            string assetsAbs = Application.dataPath.Replace("\\", "/");
            DirectoryInfo parent = Directory.GetParent(assetsAbs);
            return parent?.FullName?.Replace("\\", "/") ?? assetsAbs;
        }

        // ---------------------------------------------------------------------
        // Lock / queue
        // ---------------------------------------------------------------------

        private static bool TryEnterApplyLock(string logicalKey, bool userInitiated, string entryId, Settings settings)
        {
            logicalKey ??= string.Empty;

            double now = EditorApplication.timeSinceStartup;
            if (IsLocked(logicalKey, now, out double remaining))
            {
                string msg =
                    "This entry was applied very recently.\n\n" +
                    $"LogicalKey: {logicalKey}\n" +
                    $"Lock remaining: {remaining:0.00}s\n\n" +
                    "Apply again anyway?";

                bool ok = EditorUtility.DisplayDialog("AI Code Paste - Apply Rate Limit", msg, "Apply Anyway", "Cancel");
                if (!ok)
                    return false;

                s_LastApplyStartByKey[logicalKey] = now;
                return true;
            }

            s_LastApplyStartByKey[logicalKey] = now;
            return true;
        }

        private static bool IsLocked(string logicalKey, double now, out double remaining)
        {
            remaining = 0;

            if (string.IsNullOrEmpty(logicalKey))
                return false;

            if (!s_LastApplyStartByKey.TryGetValue(logicalKey, out double last))
                return false;

            double dt = now - last;
            if (dt >= ApplyLockWindowSeconds)
                return false;

            remaining = Math.Max(0, ApplyLockWindowSeconds - dt);
            return true;
        }

        private static void EnqueueApply(string entryId, Settings settings, string logicalKey, double now)
        {
            if (string.IsNullOrEmpty(entryId))
                return;

            EnsureUpdateHooked();

            foreach (QueuedApply q in s_QueuedApplies)
            {
                if (string.Equals(q.EntryId, entryId, StringComparison.Ordinal))
                    return;
            }

            s_QueuedApplies.Enqueue(new QueuedApply
            {
                EntryId = entryId,
                Settings = settings,
                LogicalKey = logicalKey ?? string.Empty,
                EnqueuedAt = now,
                Attempts = 0
            });
        }

        private static void EnsureUpdateHooked()
        {
            if (s_UpdateHooked)
                return;

            s_UpdateHooked = true;
            EditorApplication.update -= PumpQueue;
            EditorApplication.update += PumpQueue;
        }

        private static void PumpQueue()
        {
            if (s_QueuedApplies.Count == 0)
                return;

            int budget = 2;
            double now = EditorApplication.timeSinceStartup;

            while (budget-- > 0 && s_QueuedApplies.Count > 0)
            {
                QueuedApply q = s_QueuedApplies.Dequeue();

                if (string.IsNullOrEmpty(q.EntryId))
                    continue;

                if (IsLocked(q.LogicalKey, now, out _))
                {
                    q.Attempts++;
                    if (q.Attempts > MaxQueuedAttempts)
                    {
                        string report =
                            "Auto-apply was throttled too many times.\n\n" +
                            $"Entry: {q.EntryId}\n" +
                            $"LogicalKey: {q.LogicalKey}\n" +
                            "Reason: Repeated applies in a short time window.\n" +
                            "Action: Apply manually from the Hub if needed.";

                        ReportFailure(report, q.EntryId, setEntryError: false);
                        continue;
                    }

                    s_QueuedApplies.Enqueue(q);
                    continue;
                }

                ClipboardHistoryStore.Entry entry = ClipboardHistoryStore.instance.GetById(q.EntryId);
                if (entry == null)
                    continue;

                TryApplyEntry(entry, q.Settings, userInitiated: false);
            }
        }

        // ---------------------------------------------------------------------
        // Target path resolution (C# full file)
        // ---------------------------------------------------------------------

        public static string ResolveTargetAssetPath(string csharpText, string fallbackFolder, out string note)
        {
            note = string.Empty;

            if (TryExtractHeaderFilePath(csharpText, out string headerPath))
            {
                string normalized = NormalizeUnityAssetPath(headerPath);

                if (!normalized.StartsWith("Assets/", StringComparison.Ordinal))
                    normalized = "Assets/" + normalized.TrimStart('/');

                if (!normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    normalized += ".cs";

                note = "// File header resolved target.";
                return normalized;
            }

            string typeName = ExtractPrimaryTypeName(csharpText);
            if (!string.IsNullOrEmpty(typeName))
            {
                string ns = ExtractPrimaryNamespace(csharpText);
                string found = FindScriptAssetPathByFileName(typeName, ns);
                if (!string.IsNullOrEmpty(found))
                {
                    note = "Resolved by searching existing script file.";
                    return found;
                }
            }

            if (string.IsNullOrEmpty(typeName))
                typeName = "NewFile";

            string folder = string.IsNullOrEmpty(fallbackFolder) ? "Assets/Scripts/Generated/" : fallbackFolder;
            folder = NormalizeUnityAssetPath(folder);

            if (!folder.StartsWith("Assets/", StringComparison.Ordinal))
                folder = "Assets/" + folder.TrimStart('/');

            if (!folder.EndsWith("/", StringComparison.Ordinal))
                folder += "/";

            note = "Resolved by fallback folder (new file).";
            return folder + typeName + ".cs";
        }

        private static string FindScriptAssetPathByFileName(string typeName, string namespaceName)
        {
            string[] guids = AssetDatabase.FindAssets($"{typeName} t:MonoScript");
            string bestPath = string.Empty;
            int bestScore = int.MinValue;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                    continue;

                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                string file = Path.GetFileNameWithoutExtension(path);
                if (!string.Equals(file, typeName, StringComparison.Ordinal))
                    continue;

                int score = 100;

                try
                {
                    string abs = ToAbsolutePathStrict(path);
                    if (File.Exists(abs))
                    {
                        string content = File.ReadAllText(abs);

                        if (!string.IsNullOrEmpty(namespaceName) &&
                            content.IndexOf("namespace " + namespaceName, StringComparison.Ordinal) >= 0)
                            score += 50;

                        if (content.IndexOf("class " + typeName, StringComparison.Ordinal) >= 0 ||
                            content.IndexOf("struct " + typeName, StringComparison.Ordinal) >= 0 ||
                            content.IndexOf("interface " + typeName, StringComparison.Ordinal) >= 0 ||
                            content.IndexOf("enum " + typeName, StringComparison.Ordinal) >= 0)
                            score += 20;
                    }
                }
                catch
                {
                }

                bool isBetter =
                    score > bestScore ||
                    (score == bestScore && (string.IsNullOrEmpty(bestPath) || path.Length < bestPath.Length));

                if (isBetter)
                {
                    bestScore = score;
                    bestPath = path;
                }
            }

            return bestPath;
        }

        private static string ExtractPrimaryTypeName(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            Match m = TypeRegex.Match(text);
            if (!m.Success)
                return string.Empty;

            return m.Groups[2].Value;
        }

        private static string ExtractPrimaryNamespace(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            Match m = NamespaceRegex.Match(text);
            return m.Success ? m.Groups[1].Value : string.Empty;
        }

        private static bool TryExtractHeaderFilePath(string text, out string path)
        {
            path = string.Empty;

            if (string.IsNullOrEmpty(text))
                return false;

            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = normalized.Split('\n');

            int max = Mathf.Min(lines.Length, 50);
            for (int i = 0; i < max; i++)
            {
                Match m = FileHeaderRegex.Match(lines[i]);
                if (!m.Success)
                    continue;

                string raw = m.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(raw))
                    continue;

                path = raw;
                return true;
            }

            return false;
        }

        private static string NormalizeUnityAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string p = path.Trim().Replace("\\", "/");

            if (p.Length >= 2 && ((p[0] == '"' && p[^1] == '"') || (p[0] == '\'' && p[^1] == '\'')))
                p = p.Substring(1, p.Length - 2).Trim();

            while (p.StartsWith("./", StringComparison.Ordinal))
                p = p.Substring(2);

            int idx = p.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                p = "Assets/" + p.Substring(idx + "/Assets/".Length);

            p = p.TrimStart('/');

            return p;
        }

        private static string ToAbsolutePathStrict(string assetPath)
        {
            string p = (assetPath ?? string.Empty).Replace("\\", "/").Trim();

            if (!p.StartsWith("Assets/", StringComparison.Ordinal) && !string.Equals(p, "Assets", StringComparison.Ordinal))
                throw new InvalidOperationException($"Invalid asset path: {assetPath}");

            string assetsAbs = Application.dataPath.Replace("\\", "/");
            string assetsAbsFull = Path.GetFullPath(assetsAbs);

            string rel = p.Length > "Assets".Length ? p.Substring("Assets".Length).TrimStart('/') : string.Empty;
            if (string.IsNullOrEmpty(rel))
                throw new InvalidOperationException("Cannot write to Assets root.");

            if (ContainsPathTraversal(rel))
                throw new InvalidOperationException($"Path traversal is not allowed: {assetPath}");

            string combined = Path.GetFullPath(Path.Combine(assetsAbsFull, rel));

            string prefix = assetsAbsFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;

            if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Write blocked (outside Unity project Assets): {combined}");

            return combined.Replace("\\", "/");
        }

        private static bool ContainsPathTraversal(string rel)
        {
            if (string.IsNullOrEmpty(rel))
                return false;

            string r = rel.Replace("\\", "/");

            if (r.StartsWith("../", StringComparison.Ordinal) || r.Contains("/../") || r.EndsWith("/..", StringComparison.Ordinal))
                return true;

            string[] segs = r.Split('/');
            for (int i = 0; i < segs.Length; i++)
            {
                if (string.Equals(segs[i], "..", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void ReportFailure(string report, string entryId, bool setEntryError)
        {
            if (string.IsNullOrEmpty(report))
                report = "Unknown failure.";

            ClipboardHistoryStore.instance.SetLastErrorReport(report);

            if (!string.IsNullOrEmpty(entryId) && setEntryError)
            {
                ClipboardHistoryStore.instance.UpdateEntryResult(
                    entryId,
                    ClipboardHistoryStore.EntryStatus.Error,
                    appliedAssetPath: string.Empty,
                    errorReport: report);
            }
        }
    }
}
#endif

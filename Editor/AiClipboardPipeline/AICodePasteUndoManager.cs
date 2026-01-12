#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    /// <summary>
    /// Provides reliable undo + compile gate for applied clipboard entries.
    /// Persistence model:
    /// - Snapshots are stored on disk under Library/AICodePasteUndo/<sessionId>/Assets/...
    /// - Session metadata is stored as JSON in Library/AICodePasteUndo/<sessionId>/session.json
    /// - EntryId -> sessionId mapping is stored in EditorPrefs.
    /// - Pending compile gate is stored in EditorPrefs, so it survives domain reload.
    /// </summary>
    [InitializeOnLoad]
    public static class AICodePasteUndoManager
    {
        internal static class PrefKeys
        {
            public const string AutoUndoOnCompileError = "AICodePasteHub.AutoUndoOnCompileError";

            public const string PendingEntryId = "AICodePasteHub.CompileGate.PendingEntryId";
            public const string PendingSessionId = "AICodePasteHub.CompileGate.PendingSessionId";
            public const string PendingStartedAtTicks = "AICodePasteHub.CompileGate.PendingStartedAtTicks";

            public const string EntrySessionPrefix = "AICodePasteHub.UndoSession.Entry.";
            public const string LastSessionId = "AICodePasteHub.UndoSession.LastSessionId";
            public const string LastEntryId = "AICodePasteHub.UndoSession.LastEntryId";
        }

        [Serializable]
        private sealed class UndoSessionFile
        {
            public string assetPath;
            public bool existedBeforeApply;
        }

        [Serializable]
        private sealed class UndoSessionData
        {
            public string sessionId;
            public string entryId;
            public long createdAtTicks;
            public UndoSessionFile[] files;
        }

        static AICodePasteUndoManager()
        {
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        // ---------------------------------------------------------------------
        // Public API (used by applier + hub)
        // ---------------------------------------------------------------------

        public static bool GetAutoUndoOnCompileError()
        {
            return EditorPrefs.GetBool(PrefKeys.AutoUndoOnCompileError, false);
        }

        public static void SetAutoUndoOnCompileError(bool value)
        {
            EditorPrefs.SetBool(PrefKeys.AutoUndoOnCompileError, value);
        }

        public static bool HasUndoForEntry(string entryId)
        {
            if (string.IsNullOrEmpty(entryId))
                return false;

            string sessionId = EditorPrefs.GetString(PrefKeys.EntrySessionPrefix + entryId, string.Empty);
            if (string.IsNullOrEmpty(sessionId))
                return false;

            return File.Exists(GetSessionJsonPath(sessionId));
        }

        public static bool TryUndoEntry(string entryId, out string report)
        {
            report = string.Empty;

            if (string.IsNullOrEmpty(entryId))
            {
                report = "Undo failed: entryId is empty.";
                return false;
            }

            string sessionId = EditorPrefs.GetString(PrefKeys.EntrySessionPrefix + entryId, string.Empty);
            if (string.IsNullOrEmpty(sessionId))
            {
                report = "Undo failed: no undo session mapped for this entry.";
                return false;
            }

            return TryUndoSession(sessionId, out report);
        }

        public static bool TryUndoLast(out string report)
        {
            report = string.Empty;

            string sessionId = EditorPrefs.GetString(PrefKeys.LastSessionId, string.Empty);
            if (string.IsNullOrEmpty(sessionId))
            {
                report = "Undo failed: no last session recorded.";
                return false;
            }

            return TryUndoSession(sessionId, out report);
        }

        /// <summary>
        /// Creates a persistent undo session for the specified entry + affected asset paths.
        /// Must be called BEFORE writing/applying changes.
        /// </summary>
        public static bool TryCreateUndoSession(string entryId, IReadOnlyList<string> affectedAssetPaths,
            out string sessionId, out string error)
        {
            sessionId = string.Empty;
            error = string.Empty;

            if (string.IsNullOrEmpty(entryId))
            {
                error = "Undo session create failed: entryId is empty.";
                return false;
            }

            if (affectedAssetPaths == null || affectedAssetPaths.Count == 0)
            {
                error = "Undo session create failed: no affected files were provided.";
                return false;
            }

            try
            {
                string root = GetUndoRoot();
                Directory.CreateDirectory(root);

                sessionId = Guid.NewGuid().ToString("N");
                string sessionDir = Path.Combine(root, sessionId);
                Directory.CreateDirectory(sessionDir);

                List<UndoSessionFile> files = new(affectedAssetPaths.Count);

                for (int i = 0; i < affectedAssetPaths.Count; i++)
                {
                    string assetPath = NormalizeUnityAssetPath(affectedAssetPaths[i]);
                    if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                    {
                        error = "Undo session create blocked: path is not under Assets/: " + assetPath;
                        return false;
                    }

                    if (IsAssetsRoot(assetPath))
                    {
                        error = "Undo session create blocked: cannot target Assets root.";
                        return false;
                    }

                    if (ContainsPathTraversal(assetPath.Substring("Assets/".Length)))
                    {
                        error = "Undo session create blocked: path traversal detected: " + assetPath;
                        return false;
                    }

                    string projectAbs = AssetPathToAbsolute(assetPath);
                    bool existed = File.Exists(projectAbs);

                    // Backup path: Library/AICodePasteUndo/<sessionId>/Assets/<...>
                    string backupAbs = Path.Combine(sessionDir,
                        assetPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    string backupDir = Path.GetDirectoryName(backupAbs);
                    if (!string.IsNullOrEmpty(backupDir))
                        Directory.CreateDirectory(backupDir);

                    if (existed)
                    {
                        File.Copy(projectAbs, backupAbs, true);
                    }
                    else
                    {
                        if (!File.Exists(backupAbs))
                            File.WriteAllText(backupAbs, string.Empty);
                    }

                    files.Add(new UndoSessionFile
                    {
                        assetPath = assetPath,
                        existedBeforeApply = existed
                    });
                }

                UndoSessionData data = new()
                {
                    sessionId = sessionId,
                    entryId = entryId,
                    createdAtTicks = DateTime.UtcNow.Ticks,
                    files = files.ToArray()
                };

                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(GetSessionJsonPath(sessionId), json);

                EditorPrefs.SetString(PrefKeys.EntrySessionPrefix + entryId, sessionId);
                EditorPrefs.SetString(PrefKeys.LastSessionId, sessionId);
                EditorPrefs.SetString(PrefKeys.LastEntryId, entryId);

                return true;
            }
            catch (Exception ex)
            {
                error = "Undo session create failed:\n\n" + ex;
                sessionId = string.Empty;
                return false;
            }
        }

        /// <summary>
        /// Starts a compile gate for a specific entry/session.
        /// The gate is resolved on CompilationPipeline.compilationFinished.
        /// </summary>
        public static void BeginCompileGate(string entryId, string sessionId)
        {
            if (string.IsNullOrEmpty(entryId) || string.IsNullOrEmpty(sessionId))
                return;

            EditorPrefs.SetString(PrefKeys.PendingEntryId, entryId);
            EditorPrefs.SetString(PrefKeys.PendingSessionId, sessionId);
            EditorPrefs.SetString(PrefKeys.PendingStartedAtTicks, DateTime.UtcNow.Ticks.ToString());
        }

        public static void ClearCompileGate()
        {
            EditorPrefs.DeleteKey(PrefKeys.PendingEntryId);
            EditorPrefs.DeleteKey(PrefKeys.PendingSessionId);
            EditorPrefs.DeleteKey(PrefKeys.PendingStartedAtTicks);
        }

        // ---------------------------------------------------------------------
        // Compile gate resolution
        // ---------------------------------------------------------------------

        private static void OnCompilationFinished(object _)
        {
            string entryId = EditorPrefs.GetString(PrefKeys.PendingEntryId, string.Empty);
            string sessionId = EditorPrefs.GetString(PrefKeys.PendingSessionId, string.Empty);
            string startedTicksStr = EditorPrefs.GetString(PrefKeys.PendingStartedAtTicks, string.Empty);

            if (string.IsNullOrEmpty(entryId) || string.IsNullOrEmpty(sessionId))
                return;

            _ = long.TryParse(startedTicksStr, out long startedAtTicks);

            // The reporter writes the "last compilation finished" markers.
            // Event ordering is not guaranteed, so if markers are missing/stale, resolve on next tick.
            if (!TryReadCompilationMarkers(out long finishedAtTicks, out bool hadErrors) ||
                (startedAtTicks > 0 && finishedAtTicks > 0 && finishedAtTicks < startedAtTicks))
            {
                EditorApplication.delayCall += ResolveCompileGateDeferred;
                return;
            }

            ResolveCompileGateNow(entryId, sessionId, hadErrors);
        }

        private static void ResolveCompileGateDeferred()
        {
            string entryId = EditorPrefs.GetString(PrefKeys.PendingEntryId, string.Empty);
            string sessionId = EditorPrefs.GetString(PrefKeys.PendingSessionId, string.Empty);
            string startedTicksStr = EditorPrefs.GetString(PrefKeys.PendingStartedAtTicks, string.Empty);

            if (string.IsNullOrEmpty(entryId) || string.IsNullOrEmpty(sessionId))
                return;

            _ = long.TryParse(startedTicksStr, out long startedAtTicks);

            if (!TryReadCompilationMarkers(out long finishedAtTicks, out bool hadErrors))
                return;

            if (startedAtTicks > 0 && finishedAtTicks > 0 && finishedAtTicks < startedAtTicks)
                return;

            ResolveCompileGateNow(entryId, sessionId, hadErrors);
        }

        private static bool TryReadCompilationMarkers(out long finishedAtTicks, out bool hadErrors)
        {
            finishedAtTicks = 0;
            hadErrors = false;

            string finishedStr =
                EditorPrefs.GetString(AiFriendlyCompilationErrorReporter.PrefKeys.LastCompilationFinishedAtTicks,
                    string.Empty);
            if (!long.TryParse(finishedStr, out finishedAtTicks))
                finishedAtTicks = 0;

            hadErrors = EditorPrefs.GetBool(AiFriendlyCompilationErrorReporter.PrefKeys.LastCompilationHadErrors,
                false);
            return finishedAtTicks > 0;
        }

        private static void ResolveCompileGateNow(string entryId, string sessionId, bool hadErrors)
        {
            if (!hadErrors)
            {
                ClearCompileGate();
                return;
            }

            bool autoUndo = GetAutoUndoOnCompileError();

            if (autoUndo)
            {
                bool ok = TryUndoSession(sessionId, out string undoReport);

                Debug.LogError(
                    "AI Code Paste - Auto-Undo performed due to compilation errors.\n\n" +
                    "Undo result:\n" + (string.IsNullOrEmpty(undoReport) ? "(no details)" : undoReport));

                MarkEntryUndone(entryId,
                    ok ? "Auto-undone due to compilation errors." : "Auto-undo attempted; see Console.");
                ClearCompileGate();
                return;
            }

            // No prompt: mark the entry as Error so the Hub shows Undo button for it.
            MarkEntryCompileError(entryId);

            Debug.LogError(
                "AI Code Paste - Compilation errors detected after apply.\n" +
                "Use the Hub history row 'Undo' button to rollback the applied session.\n" +
                $"EntryId: {entryId}\nSessionId: {sessionId}");

            ClearCompileGate();
        }

        private static void MarkEntryCompileError(string entryId)
        {
            ClipboardHistoryStore.instance.UpdateEntryResult(
                entryId,
                ClipboardHistoryStore.EntryStatus.Error,
                "(applied) compile errors",
                string.Empty);
        }

        // ---------------------------------------------------------------------
        // Undo implementation
        // ---------------------------------------------------------------------

        private static bool TryUndoSession(string sessionId, out string report)
        {
            report = string.Empty;

            if (string.IsNullOrEmpty(sessionId))
            {
                report = "Undo failed: sessionId is empty.";
                return false;
            }

            string jsonPath = GetSessionJsonPath(sessionId);
            if (!File.Exists(jsonPath))
            {
                report = "Undo failed: session file not found: " + jsonPath;
                return false;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                UndoSessionData data = JsonUtility.FromJson<UndoSessionData>(json);

                if (data == null || data.files == null || data.files.Length == 0)
                {
                    report = "Undo failed: session metadata is invalid or empty.";
                    return false;
                }

                string root = GetUndoRoot();
                string sessionDir = Path.Combine(root, sessionId);

                int restored = 0;
                int deleted = 0;
                List<string> touched = new(data.files.Length);

                for (int i = 0; i < data.files.Length; i++)
                {
                    string assetPath = NormalizeUnityAssetPath(data.files[i].assetPath);
                    if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                        continue;

                    string projectAbs = AssetPathToAbsolute(assetPath);
                    bool existedBefore = data.files[i].existedBeforeApply;

                    if (existedBefore)
                    {
                        string backupAbs = Path.Combine(sessionDir,
                            assetPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
                        if (!File.Exists(backupAbs))
                            continue;

                        string projDir = Path.GetDirectoryName(projectAbs);
                        if (!string.IsNullOrEmpty(projDir))
                            Directory.CreateDirectory(projDir);

                        File.Copy(backupAbs, projectAbs, true);
                        restored++;
                        touched.Add(assetPath);
                    }
                    else
                    {
                        if (File.Exists(projectAbs))
                        {
                            File.Delete(projectAbs);
                            deleted++;
                            touched.Add(assetPath);
                        }
                    }
                }

                for (int i = 0; i < touched.Count; i++)
                {
                    AssetDatabase.ImportAsset(touched[i], ImportAssetOptions.ForceUpdate);
                }

                AssetDatabase.Refresh();

                report =
                    "Undo session completed.\n" +
                    "SessionId: " + sessionId + "\n" +
                    "Restored: " + restored + "\n" +
                    "Deleted : " + deleted + "\n" +
                    "Files:\n - " + string.Join("\n - ", touched);

                return true;
            }
            catch (Exception ex)
            {
                report = "Undo failed:\n\n" + ex;
                return false;
            }
        }

        private static void MarkEntryUndone(string entryId, string note)
        {
            ClipboardHistoryStore.instance.UpdateEntryResult(
                entryId,
                ClipboardHistoryStore.EntryStatus.Pending,
                "(undone) " + (note ?? string.Empty),
                string.Empty);
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static string GetUndoRoot()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "Library", "AICodePasteUndo");
        }

        private static string GetSessionJsonPath(string sessionId)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "Library", "AICodePasteUndo", sessionId, "session.json");
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            string assetsAbs = Application.dataPath.Replace("\\", "/");
            string rel = assetPath.Substring("Assets/".Length);
            return Path.Combine(assetsAbs, rel).Replace("\\", "/");
        }

        private static string NormalizeUnityAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string p = path.Trim().Replace("\\", "/");
            while (p.StartsWith("./", StringComparison.Ordinal))
            {
                p = p.Substring(2);
            }

            int idx = p.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                p = "Assets/" + p.Substring(idx + "/Assets/".Length);

            p = p.TrimStart('/');
            return p;
        }

        private static bool IsAssetsRoot(string assetPath)
        {
            return string.Equals(assetPath, "Assets", StringComparison.Ordinal) ||
                   string.Equals(assetPath, "Assets/", StringComparison.Ordinal);
        }

        private static bool ContainsPathTraversal(string rel)
        {
            if (string.IsNullOrEmpty(rel))
                return false;

            string r = rel.Replace("\\", "/");

            if (r.StartsWith("../", StringComparison.Ordinal) || r.Contains("/../") ||
                r.EndsWith("/..", StringComparison.Ordinal))
                return true;

            string[] segs = r.Split('/');
            for (int i = 0; i < segs.Length; i++)
            {
                if (string.Equals(segs[i], "..", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
#endif
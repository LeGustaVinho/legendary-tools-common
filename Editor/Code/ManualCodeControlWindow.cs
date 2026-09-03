#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Provides an explicit code import, compilation, and domain reload workflow.
    /// External code changes are detected directly from the file system, so this
    /// window continues to work while Unity's Auto Refresh option is disabled.
    /// </summary>
    public sealed class ManualCodeControlWindow : EditorWindow
    {
        private Vector2 _scrollPosition;

        [MenuItem("Tools/Legendary Tools/Code/Manual Code Control")]
        public static void Open()
        {
            ManualCodeControlWindow window = GetWindow<ManualCodeControlWindow>();
            window.titleContent = new GUIContent("Manual Code Control");
            window.minSize = new Vector2(560f, 320f);
            window.Show();
        }

        private void OnEnable()
        {
            ManualCodeChangeMonitor.Changed -= Repaint;
            ManualCodeChangeMonitor.Changed += Repaint;
        }

        private void OnDisable()
        {
            ManualCodeChangeMonitor.Changed -= Repaint;
        }

        private void OnGUI()
        {
            DrawSettingsStatus();
            EditorGUILayout.Space(8f);
            DrawActions();
            EditorGUILayout.Space(8f);
            DrawPendingChanges();
        }

        private static void DrawSettingsStatus()
        {
            EditorGUILayout.LabelField("Manual workflow", EditorStyles.boldLabel);

            bool autoRefreshDisabled = ManualCodeControlSettings.IsAutoRefreshDisabled;
            bool domainReloadDisabled = ManualCodeControlSettings.IsDomainReloadOnPlayDisabled;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawStatusLine("Asset Auto Refresh", autoRefreshDisabled ? "Disabled" : "Enabled", autoRefreshDisabled);
                DrawStatusLine("Domain Reload on Play", domainReloadDisabled ? "Disabled" : "Enabled", domainReloadDisabled);

                if (!autoRefreshDisabled || !domainReloadDisabled)
                {
                    EditorGUILayout.Space(3f);
                    if (GUILayout.Button("Apply Manual Workflow Settings"))
                    {
                        ManualCodeControlSettings.Apply();
                    }
                }
            }
        }

        private static void DrawStatusLine(string label, string value, bool expected)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(190f));
                Color previousColor = GUI.color;
                GUI.color = expected ? new Color(0.55f, 1f, 0.55f) : new Color(1f, 0.65f, 0.45f);
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
                GUI.color = previousColor;
            }
        }

        private static void DrawActions()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            bool editorBusy = EditorApplication.isCompiling || EditorApplication.isUpdating;
            bool inPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;

            using (new EditorGUI.DisabledScope(editorBusy || inPlayMode))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import and Compile", GUILayout.Height(34f)))
                {
                    ManualCodeChangeMonitor.ImportAndCompile();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("Reload Domain", GUILayout.Height(34f)))
                {
                    ManualCodeChangeMonitor.ReloadDomain();
                    GUIUtility.ExitGUI();
                }
            }

            string state = EditorApplication.isCompiling
                ? "Unity is compiling scripts..."
                : EditorApplication.isUpdating
                    ? "Unity is importing assets..."
                    : inPlayMode
                        ? "Manual actions are available in Edit Mode."
                        : "Ready.";

            EditorGUILayout.HelpBox(
                state + " Import and Compile refreshes the Asset Database before requesting compilation. " +
                "Reload Domain resets the managed domain without importing pending file changes.",
                MessageType.Info);
        }

        private void DrawPendingChanges()
        {
            IReadOnlyList<ManualCodeChange> changes = ManualCodeChangeMonitor.PendingChanges;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Pending code changes ({changes.Count})", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Clear List", GUILayout.Width(90f)))
                {
                    ManualCodeChangeMonitor.Clear();
                }
            }

            if (changes.Count == 0)
            {
                EditorGUILayout.HelpBox("No code changes have been detected since the last import.", MessageType.None);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            foreach (ManualCodeChange change in changes)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(change.Kind.ToString(), GUILayout.Width(70f));
                    EditorGUILayout.SelectableLabel(change.RelativePath, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                    if (GUILayout.Button("Reveal", GUILayout.Width(58f)))
                    {
                        EditorUtility.RevealInFinder(change.AbsolutePath);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }

    public enum ManualCodeChangeKind
    {
        Modified,
        Created,
        Deleted
    }

    public sealed class ManualCodeChange
    {
        public ManualCodeChange(string absolutePath, string relativePath, ManualCodeChangeKind kind)
        {
            AbsolutePath = absolutePath;
            RelativePath = relativePath;
            Kind = kind;
        }

        public string AbsolutePath { get; }
        public string RelativePath { get; }
        public ManualCodeChangeKind Kind { get; }
    }

    [InitializeOnLoad]
    internal static class ManualCodeControlSettings
    {
        private const string AutoRefreshModePreference = "kAutoRefreshMode";
        private const int AutoRefreshDisabled = 0;

        static ManualCodeControlSettings()
        {
            Apply();
        }

        internal static bool IsAutoRefreshDisabled =>
            EditorPrefs.GetInt(AutoRefreshModePreference, AutoRefreshDisabled) == AutoRefreshDisabled;

        internal static bool IsDomainReloadOnPlayDisabled =>
            EditorSettings.enterPlayModeOptionsEnabled &&
            (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;

        internal static void Apply()
        {
            EditorPrefs.SetInt(AutoRefreshModePreference, AutoRefreshDisabled);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }
    }

    [InitializeOnLoad]
    internal static class ManualCodeChangeMonitor
    {
        private static readonly string[] CodeExtensions =
        {
            ".cs",
            ".asmdef",
            ".asmref",
            ".rsp"
        };

        private static readonly ConcurrentQueue<RawFileChange> QueuedChanges = new();
        private static readonly ConcurrentQueue<string> WatcherErrors = new();
        private static readonly Dictionary<string, ManualCodeChange> ChangesByPath =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<FileSystemWatcher> Watchers = new();

        private static volatile bool _ignoreWatcherEvents;

        static ManualCodeChangeMonitor()
        {
            CreateWatcher(Path.Combine(ProjectRoot, "Assets"));
            CreateWatcher(Path.Combine(ProjectRoot, "Packages"));

            EditorApplication.update -= FlushQueuedChanges;
            EditorApplication.update += FlushQueuedChanges;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        internal static event Action Changed;

        internal static IReadOnlyList<ManualCodeChange> PendingChanges => ChangesByPath.Values
            .OrderBy(change => change.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;

        internal static void ImportAndCompile()
        {
            _ignoreWatcherEvents = true;
            ClearQueuedChanges();
            ChangesByPath.Clear();
            Changed?.Invoke();

            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                CompilationPipeline.RequestScriptCompilation();
            }
            finally
            {
                EditorApplication.delayCall += ResumeMonitoring;
            }
        }

        internal static void ReloadDomain()
        {
            EditorUtility.RequestScriptReload();
        }

        internal static void Clear()
        {
            ClearQueuedChanges();
            ChangesByPath.Clear();
            Changed?.Invoke();
        }

        private static void CreateWatcher(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            FileSystemWatcher watcher = new(directory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnWatcherError;
            Watchers.Add(watcher);
        }

        private static void OnChanged(object sender, FileSystemEventArgs args)
        {
            if (!_ignoreWatcherEvents && IsCodeFile(args.FullPath))
                QueuedChanges.Enqueue(new RawFileChange(args.FullPath, args.ChangeType));
        }

        private static void OnRenamed(object sender, RenamedEventArgs args)
        {
            if (_ignoreWatcherEvents)
                return;

            if (IsCodeFile(args.OldFullPath))
                QueuedChanges.Enqueue(new RawFileChange(args.OldFullPath, WatcherChangeTypes.Deleted));

            if (IsCodeFile(args.FullPath))
                QueuedChanges.Enqueue(new RawFileChange(args.FullPath, WatcherChangeTypes.Created));
        }

        private static void OnWatcherError(object sender, ErrorEventArgs args)
        {
            WatcherErrors.Enqueue(args.GetException().Message);
        }

        private static void FlushQueuedChanges()
        {
            while (WatcherErrors.TryDequeue(out string error))
                Debug.LogWarning($"Manual Code Control file watcher error: {error}");

            if (_ignoreWatcherEvents)
                return;

            bool changed = false;
            while (QueuedChanges.TryDequeue(out RawFileChange rawChange))
            {
                string fullPath = Path.GetFullPath(rawChange.AbsolutePath);
                string relativePath = MakeRelativePath(fullPath);
                ManualCodeChangeKind kind = ToChangeKind(rawChange.ChangeType, fullPath);

                ChangesByPath[fullPath] = new ManualCodeChange(fullPath, relativePath, kind);
                changed = true;
            }

            if (changed)
                Changed?.Invoke();
        }

        private static ManualCodeChangeKind ToChangeKind(WatcherChangeTypes changeType, string fullPath)
        {
            if (changeType == WatcherChangeTypes.Deleted || !File.Exists(fullPath))
                return ManualCodeChangeKind.Deleted;

            return changeType == WatcherChangeTypes.Created
                ? ManualCodeChangeKind.Created
                : ManualCodeChangeKind.Modified;
        }

        private static bool IsCodeFile(string path)
        {
            string extension = Path.GetExtension(path);
            return CodeExtensions.Any(candidate =>
                string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));
        }

        private static string MakeRelativePath(string fullPath)
        {
            string root = ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return fullPath;

            return fullPath.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
        }

        private static void ResumeMonitoring()
        {
            ClearQueuedChanges();
            _ignoreWatcherEvents = false;
        }

        private static void ClearQueuedChanges()
        {
            while (QueuedChanges.TryDequeue(out _))
            {
            }
        }

        private static void Dispose()
        {
            EditorApplication.update -= FlushQueuedChanges;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;

            foreach (FileSystemWatcher watcher in Watchers)
                watcher.Dispose();

            Watchers.Clear();
        }

        private readonly struct RawFileChange
        {
            internal RawFileChange(string absolutePath, WatcherChangeTypes changeType)
            {
                AbsolutePath = absolutePath;
                ChangeType = changeType;
            }

            internal string AbsolutePath { get; }
            internal WatcherChangeTypes ChangeType { get; }
        }
    }
}
#endif

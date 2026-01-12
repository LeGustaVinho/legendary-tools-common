// File: Assets/legendary-tools-common/Editor/AiClipboardPipeline/AICodePasteHubWindow.cs
using System;
using UnityEditor;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    public sealed class AICodePasteHubWindow : EditorWindow
    {
        private const float LeftPaneWidthRatio = 0.42f;

        private AICodePasteHubController _controller;

        private Vector2 _historyScroll;
        private Vector2 _previewScroll;

        private GUIStyle _monoWrapStyle;
        private Texture2D _selectedBgTexture;

        [MenuItem("Tools/AI Clipboard Pipeline/Hub")]
        public static void Open()
        {
            AICodePasteHubWindow w = GetWindow<AICodePasteHubWindow>("AI Code Paste Hub");
            w.minSize = new Vector2(1060, 560);
            w.Show();
        }

        private void OnEnable()
        {
            _controller = new AICodePasteHubController();
            _controller.StateChanged += Repaint;

            // IMPORTANT:
            // Do not touch EditorStyles / GUI.skin here.
            // During domain reload or early editor init, EditorStyles.textArea can be null and throw.
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.StateChanged -= Repaint;
                _controller.Dispose();
                _controller = null;
            }

            if (_selectedBgTexture != null)
            {
                DestroyImmediate(_selectedBgTexture);
                _selectedBgTexture = null;
            }

            _monoWrapStyle = null;
        }

        private void OnGUI()
        {
            if (_controller == null)
            {
                EditorGUILayout.HelpBox("Controller is not initialized.", MessageType.Warning);
                return;
            }

            EnsureStylesReady();
            if (_monoWrapStyle == null)
            {
                // Editor GUI is not fully ready yet; skip drawing this frame.
                // This avoids null refs during early initialization.
                Repaint();
                return;
            }

            DrawTopStatusBar();
            EditorGUILayout.Space(6);

            Rect area = new(
                0,
                EditorGUIUtility.singleLineHeight + 8,
                position.width,
                position.height - (EditorGUIUtility.singleLineHeight + 8));

            DrawTopArea(area);
        }

        private void DrawTopStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(new GUIContent($"Listener: {_controller.ListenerStatus}"), EditorStyles.toolbarButton);

                GUILayout.FlexibleSpace();

                bool newEnabled = GUILayout.Toggle(
                    _controller.Enabled,
                    _controller.Enabled ? "Disable" : "Enable",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(90));

                if (newEnabled != _controller.Enabled)
                    _controller.SetEnabled(newEnabled);
            }
        }

        private void DrawTopArea(Rect area)
        {
            GUILayout.BeginArea(area);
            using (new EditorGUILayout.HorizontalScope())
            {
                float leftWidth = Mathf.Max(360f, area.width * LeftPaneWidthRatio);

                using (new EditorGUILayout.VerticalScope(GUILayout.Width(leftWidth)))
                {
                    DrawControlPanel();
                    EditorGUILayout.Space(8);
                    DrawHistoryPanel();
                    EditorGUILayout.Space(8);
                    DrawBulkPanel();
                    EditorGUILayout.Space(8);
                    DrawActionsPanel(); // Kept, but buttons removed as requested.
                }

                GUILayout.Space(10);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    DrawPreviewPanel();
                }
            }

            GUILayout.EndArea();
        }

        private void DrawControlPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Control", EditorStyles.boldLabel);

                using (new EditorGUI.DisabledScope(!_controller.Enabled))
                {
                    bool newAutoCapture = EditorGUILayout.ToggleLeft("Auto-capture", _controller.AutoCapture);
                    bool newAutoApply = EditorGUILayout.ToggleLeft("Auto-apply", _controller.AutoApply);

                    bool newAutoUndo = EditorGUILayout.ToggleLeft(
                        "Auto-undo on compile errors",
                        _controller.AutoUndoOnCompileError);

                    if (newAutoCapture != _controller.AutoCapture)
                        _controller.SetAutoCapture(newAutoCapture);

                    if (newAutoApply != _controller.AutoApply)
                        _controller.SetAutoApply(newAutoApply);

                    if (newAutoUndo != _controller.AutoUndoOnCompileError)
                        _controller.SetAutoUndoOnCompileError(newAutoUndo);
                }

                EditorGUILayout.Space(8);
                GUILayout.Label("Settings", EditorStyles.boldLabel);

                using (new EditorGUI.DisabledScope(!_controller.Enabled))
                {
                    int newMax = EditorGUILayout.IntField(new GUIContent("Max history (N)"), _controller.MaxHistory);
                    newMax = Mathf.Clamp(newMax, 1, 5000);
                    if (newMax != _controller.MaxHistory)
                        _controller.SetMaxHistory(newMax);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string newFolder = EditorGUILayout.TextField(
                            new GUIContent("Fallback folder"),
                            _controller.FallbackFolder);

                        if (GUILayout.Button("...", GUILayout.Width(32)))
                        {
                            string picked = EditorUtility.OpenFolderPanel(
                                "Select fallback folder (must be under Assets)",
                                string.Empty,
                                string.Empty);

                            if (!string.IsNullOrEmpty(picked))
                            {
                                if (TryConvertAbsoluteFolderToAssetsPath(picked, out string assetsFolder))
                                    _controller.SetFallbackFolder(EnsureTrailingSlash(assetsFolder));
                                else
                                    EditorUtility.DisplayDialog(
                                        "Invalid folder",
                                        "Selected folder must be inside this project's Assets folder.",
                                        "OK");
                            }
                        }

                        if (newFolder != _controller.FallbackFolder)
                            _controller.SetFallbackFolder(newFolder);
                    }
                }
            }
        }

        private void DrawHistoryPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("History", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"Captured items: {_controller.Items.Count}", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                }

                Rect box = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.Height(270));
                GUI.Box(box, GUIContent.none);

                Rect inner = new(box.x + 2, box.y + 2, box.width - 4, box.height - 4);

                const float rowH = 48f;
                float contentH = Mathf.Max(inner.height, _controller.Items.Count * rowH);
                Rect viewRect = new(0, 0, inner.width - 16f, contentH);

                _historyScroll = GUI.BeginScrollView(inner, _historyScroll, viewRect);

                for (int i = 0; i < _controller.Items.Count; i++)
                {
                    AICodePasteHubController.HistoryItem item = _controller.Items[i];
                    Rect rowRect = new(0, i * rowH, viewRect.width, rowH);

                    const float btnW = 72f;
                    const float btnH = 22f;
                    float btnY = rowRect.y + (rowRect.height - btnH) * 0.5f;

                    Rect actionRect = new(rowRect.xMax - btnW * 2f - 10f, btnY, btnW, btnH);
                    Rect delRect = new(rowRect.xMax - btnW - 6f, btnY, btnW, btnH);

                    float reservedRight = btnW * 2f + 18f;
                    Rect selectRect = new(rowRect.x, rowRect.y, Mathf.Max(1f, rowRect.width - reservedRight),
                        rowRect.height);

                    bool isSelected = i == _controller.SelectedIndex;
                    if (isSelected && _selectedBgTexture != null)
                        GUI.DrawTexture(rowRect, _selectedBgTexture, ScaleMode.StretchToFill);

                    if (GUI.Button(selectRect, GUIContent.none, GUIStyle.none))
                        _controller.SelectIndex(i);

                    Rect statusRect = new(rowRect.x + 6, rowRect.y + 14, 18, 18);
                    GUI.Label(statusRect, GetStatusIcon(item.Status));

                    Rect line1 = new(rowRect.x + 28, rowRect.y + 6, rowRect.width - reservedRight - 34, 18);
                    GUI.Label(line1, $"{GetTypeLabel(item.Type)} • {item.Title}", EditorStyles.miniBoldLabel);

                    Rect line2 = new(rowRect.x + 28, rowRect.y + 24, rowRect.width - reservedRight - 34, 18);
                    GUI.Label(line2, $"{item.Timestamp:yyyy-MM-dd HH:mm:ss} • {item.Status}", EditorStyles.miniLabel);

                    bool canApply = _controller.CanApply(item);
                    bool canUndo = _controller.CanUndo(item);

                    bool showUndo = canUndo &&
                                    (item.Status == AICodePasteHubController.HistoryStatus.Applied ||
                                     item.Status == AICodePasteHubController.HistoryStatus.Error);

                    if (showUndo)
                    {
                        if (GUI.Button(actionRect, "Undo"))
                            _controller.UndoById(item.Id);
                    }
                    else
                    {
                        bool isApplied = item.Status == AICodePasteHubController.HistoryStatus.Applied;

                        using (new EditorGUI.DisabledScope(!canApply || isApplied))
                        {
                            if (GUI.Button(actionRect, "Apply"))
                                _controller.ApplyById(item.Id);
                        }
                    }

                    if (GUI.Button(delRect, "Delete"))
                        _controller.DeleteById(item.Id);
                }

                GUI.EndScrollView();
            }
        }

        private void DrawBulkPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Bulk", EditorStyles.boldLabel);

                bool hasAnyApplicable = false;
                for (int i = 0; i < _controller.Items.Count; i++)
                {
                    if (_controller.CanApply(_controller.Items[i]))
                    {
                        hasAnyApplicable = true;
                        break;
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!hasAnyApplicable))
                    {
                        if (GUILayout.Button("Apply All Pending", GUILayout.Height(26)))
                            _controller.ApplyAllPending();
                    }

                    using (new EditorGUI.DisabledScope(_controller.Items.Count == 0))
                    {
                        if (GUILayout.Button("Delete All History", GUILayout.Height(26)))
                        {
                            bool ok = EditorUtility.DisplayDialog(
                                "Delete All History",
                                "This will remove all captured clipboard history entries.\n\nContinue?",
                                "Delete",
                                "Cancel");

                            if (ok)
                                _controller.ClearAllHistory();
                        }
                    }
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Copy Unified Compilation Errors", GUILayout.Height(26)))
                    {
                        bool copied = _controller.CopyUnifiedCompilationErrorsToClipboard();
                        if (copied)
                            ShowNotification(new GUIContent("Compilation error report copied to clipboard."));
                        else
                            EditorUtility.DisplayDialog(
                                "No Compilation Errors",
                                "No unified compilation error report is currently available.",
                                "OK");
                    }
                }
            }
        }

        private void DrawPreviewPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
            {
                GUILayout.Label("Preview", EditorStyles.boldLabel);

                AICodePasteHubController.HistoryItem selected = _controller.SelectedItem;
                if (selected == null)
                {
                    EditorGUILayout.HelpBox("Select a captured item to preview.", MessageType.Info);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"Type: {GetTypeLabel(selected.Type)}", EditorStyles.miniLabel);
                    GUILayout.Space(10);
                    GUILayout.Label($"Status: {selected.Status}", EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(6);

                _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.ExpandHeight(true));
                EditorGUILayout.TextArea(_controller.PreviewText ?? string.Empty, _monoWrapStyle,
                    GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawActionsPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Actions", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "This section intentionally has no buttons.\n" +
                    "Use History row buttons or the Bulk section above.",
                    MessageType.Info);
            }
        }

        private void EnsureStylesReady()
        {
            if (_monoWrapStyle != null && _selectedBgTexture != null)
                return;

            // EditorStyles can be unavailable during OnEnable or early init.
            GUIStyle baseStyle = null;

            try
            {
                baseStyle = EditorStyles.textArea;
            }
            catch
            {
                // Ignore: EditorStyles not ready yet.
            }

            if (baseStyle == null && GUI.skin != null)
                baseStyle = GUI.skin.textArea;

            if (baseStyle == null)
                return; // Not ready this frame.

            if (_monoWrapStyle == null)
            {
                _monoWrapStyle = new GUIStyle(baseStyle)
                {
                    wordWrap = true,
                    richText = false
                };
            }
            else
            {
                _monoWrapStyle.wordWrap = true;
                _monoWrapStyle.richText = false;
            }

            if (_selectedBgTexture == null)
                _selectedBgTexture = MakeTex(1, 1, new Color(0.22f, 0.45f, 0.95f, 0.18f));
        }

        private static Texture2D MakeTex(int width, int height, Color color)
        {
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static GUIContent GetStatusIcon(AICodePasteHubController.HistoryStatus status)
        {
            switch (status)
            {
                case AICodePasteHubController.HistoryStatus.Applied:
                    return EditorGUIUtility.IconContent("TestPassed", "Applied");
                case AICodePasteHubController.HistoryStatus.Error:
                    return EditorGUIUtility.IconContent("TestFailed", "Error");
                case AICodePasteHubController.HistoryStatus.Ignored:
                    return EditorGUIUtility.IconContent("console.warnicon", "Ignored");
                case AICodePasteHubController.HistoryStatus.Pending:
                default:
                    return EditorGUIUtility.IconContent("WaitSpin00", "Pending");
            }
        }

        private static string GetTypeLabel(AICodePasteHubController.HistoryType type)
        {
            switch (type)
            {
                case AICodePasteHubController.HistoryType.FullFile:
                    return "Full File";
                default:
                    return "Patch";
            }
        }

        private static bool TryConvertAbsoluteFolderToAssetsPath(string folderAbs, out string assetsPath)
        {
            assetsPath = string.Empty;

            if (string.IsNullOrEmpty(folderAbs))
                return false;

            string assetsAbs = Application.dataPath.Replace("\\", "/").TrimEnd('/');
            string picked = folderAbs.Replace("\\", "/").TrimEnd('/');

            if (!picked.StartsWith(assetsAbs, StringComparison.OrdinalIgnoreCase))
                return false;

            string rel = picked.Substring(assetsAbs.Length).TrimStart('/');
            assetsPath = string.IsNullOrEmpty(rel) ? "Assets" : "Assets/" + rel;
            return true;
        }

        private static string EnsureTrailingSlash(string p)
        {
            if (string.IsNullOrEmpty(p))
                return "Assets/";

            p = p.Replace("\\", "/");
            return p.EndsWith("/", StringComparison.Ordinal) ? p : p + "/";
        }
    }
}

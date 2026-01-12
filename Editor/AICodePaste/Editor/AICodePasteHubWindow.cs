using UnityEditor;
using UnityEngine;

namespace AiClipboardPipeline.Editor
{
    public sealed class AICodePasteHubWindow : EditorWindow
    {
        private const float BottomPanelHeight = 210f;
        private const float LeftPaneWidthRatio = 0.42f;

        private AICodePasteHubController _controller;

        private Vector2 _historyScroll;
        private Vector2 _previewScroll;
        private Vector2 _errorScroll;

        private GUIStyle _monoWrapStyle;
        private Texture2D _selectedBgTexture;

        private bool _stylesInitialized;
        private bool _stylesInitQueued;

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

            // Do not call EnsureStyles() here.
            // During early editor initialization/domain reload, EditorStyles may not be ready yet.
            QueueEnsureStyles();
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
            _stylesInitialized = false;
            _stylesInitQueued = false;
        }

        private void OnGUI()
        {
            if (_controller == null)
            {
                EditorGUILayout.HelpBox("Controller is not initialized.", MessageType.Warning);
                return;
            }

            EnsureStyles();

            if (!_stylesInitialized || _monoWrapStyle == null)
            {
                // If styles are still unavailable (very early editor state), avoid breaking the window.
                EditorGUILayout.HelpBox("Initializing editor styles...", MessageType.Info);
                QueueEnsureStyles();
                return;
            }

            DrawTopStatusBar();
            EditorGUILayout.Space(6);

            Rect full = new(
                0,
                EditorGUIUtility.singleLineHeight + 8,
                position.width,
                position.height - (EditorGUIUtility.singleLineHeight + 8));

            Rect bottom = new(full.x, full.yMax - BottomPanelHeight, full.width, BottomPanelHeight);
            Rect top = new(full.x, full.y, full.width, full.height - BottomPanelHeight - 6);

            DrawTopArea(top);
            GUILayout.Space(6);
            DrawBottomFixedPanel(bottom);
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
                    DrawActionsPanel();
                }

                GUILayout.Space(10);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    DrawPreviewPanel();
                }
            }

            GUILayout.EndArea();
        }

        private void DrawBottomFixedPanel(Rect area)
        {
            GUILayout.BeginArea(area);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawLastErrorReportPanel();
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
                    bool newAutoCopy = EditorGUILayout.ToggleLeft(
                        "Auto-copy error report on failure",
                        _controller.AutoCopyErrorReport);

                    if (newAutoCapture != _controller.AutoCapture)
                        _controller.SetAutoCapture(newAutoCapture);

                    if (newAutoApply != _controller.AutoApply)
                        _controller.SetAutoApply(newAutoApply);

                    if (newAutoCopy != _controller.AutoCopyErrorReport)
                        _controller.SetAutoCopyErrorReport(newAutoCopy);
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

                        GUILayout.Button("...", GUILayout.Width(32)); // Visual placeholder

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

                    // Buttons layout (right side)
                    const float btnW = 72f;
                    const float btnH = 22f;
                    float btnY = rowRect.y + (rowRect.height - btnH) * 0.5f;

                    Rect applyRect = new(rowRect.xMax - btnW * 2f - 10f, btnY, btnW, btnH);
                    Rect delRect = new(rowRect.xMax - btnW - 6f, btnY, btnW, btnH);

                    // Selection rect excludes button area.
                    float reservedRight = btnW * 2f + 18f;
                    Rect selectRect = new(
                        rowRect.x,
                        rowRect.y,
                        Mathf.Max(1f, rowRect.width - reservedRight),
                        rowRect.height);

                    bool isSelected = i == _controller.SelectedIndex;
                    if (isSelected && _selectedBgTexture != null)
                        GUI.DrawTexture(rowRect, _selectedBgTexture, ScaleMode.StretchToFill);

                    if (GUI.Button(selectRect, GUIContent.none, GUIStyle.none))
                        _controller.SelectIndex(i);

                    // Status icon
                    Rect statusRect = new(rowRect.x + 6, rowRect.y + 14, 18, 18);
                    GUI.Label(statusRect, GetStatusIcon(item.Status));

                    // Type + title
                    Rect line1 = new(rowRect.x + 28, rowRect.y + 6, rowRect.width - reservedRight - 34, 18);
                    GUI.Label(line1, $"{GetTypeLabel(item.Type)} • {item.Title}", EditorStyles.miniBoldLabel);

                    // Timestamp + status
                    Rect line2 = new(rowRect.x + 28, rowRect.y + 24, rowRect.width - reservedRight - 34, 18);
                    GUI.Label(line2, $"{item.Timestamp:yyyy-MM-dd HH:mm:ss} • {item.Status}", EditorStyles.miniLabel);

                    bool canApply = _controller.CanApply(item);
                    bool isApplied = item.Status == AICodePasteHubController.HistoryStatus.Applied;

                    using (new EditorGUI.DisabledScope(!canApply || isApplied))
                    {
                        if (GUI.Button(applyRect, "Apply"))
                            _controller.ApplyById(item.Id);
                    }

                    if (GUI.Button(delRect, "Delete"))
                        _controller.DeleteById(item.Id);
                }

                GUI.EndScrollView();
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
                EditorGUILayout.TextArea(_controller.PreviewText ?? string.Empty, _monoWrapStyle, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawActionsPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Actions", EditorStyles.boldLabel);

                AICodePasteHubController.HistoryItem selected = _controller.SelectedItem;
                bool hasSelection = selected != null;

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(
                               !hasSelection ||
                               !_controller.CanApply(selected) ||
                               selected.Status == AICodePasteHubController.HistoryStatus.Applied))
                    {
                        if (GUILayout.Button("Apply Selected", GUILayout.Height(26)))
                            _controller.ApplyById(selected.Id);
                    }

                    using (new EditorGUI.DisabledScope(!hasSelection))
                    {
                        if (GUILayout.Button("Ignore Selected", GUILayout.Height(26)))
                            _controller.IgnoreById(selected.Id);

                        if (GUILayout.Button("Delete Selected", GUILayout.Height(26)))
                            _controller.DeleteById(selected.Id);
                    }
                }
            }
        }

        private void DrawLastErrorReportPanel()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Last Error Report", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Copy", GUILayout.Width(70)))
                    _controller.CopyLastErrorReportToClipboard();

                if (GUILayout.Button("Clear", GUILayout.Width(70)))
                    _controller.ClearLastErrorReport();
            }

            EditorGUILayout.Space(6);

            Rect box = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.Box(box, GUIContent.none);

            Rect inner = new(box.x + 2, box.y + 2, box.width - 4, box.height - 4);

            GUILayout.BeginArea(inner);
            _errorScroll = EditorGUILayout.BeginScrollView(_errorScroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_controller.LastErrorReport ?? string.Empty, _monoWrapStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void QueueEnsureStyles()
        {
            if (_stylesInitQueued)
                return;

            _stylesInitQueued = true;
            EditorApplication.delayCall += EnsureStylesDelayed;
        }

        private void EnsureStylesDelayed()
        {
            _stylesInitQueued = false;

            if (this == null)
                return;

            EnsureStyles();
            Repaint();
        }

        private void EnsureStyles()
        {
            if (_stylesInitialized && _monoWrapStyle != null && _selectedBgTexture != null)
                return;

            try
            {
                // EditorStyles might be unavailable during early initialization.
                GUIStyle baseStyle = null;

                try
                {
                    baseStyle = EditorStyles.textArea;
                }
                catch
                {
                    // Ignore and fallback.
                }

                if (baseStyle == null)
                {
                    try
                    {
                        if (GUI.skin != null)
                            baseStyle = GUI.skin.textArea;
                    }
                    catch
                    {
                        // Ignore and fallback.
                    }
                }

                if (baseStyle == null)
                    baseStyle = new GUIStyle();

                _monoWrapStyle = new GUIStyle(baseStyle)
                {
                    wordWrap = true,
                    richText = false
                };

                if (_selectedBgTexture == null)
                    _selectedBgTexture = MakeTex(1, 1, new Color(0.22f, 0.45f, 0.95f, 0.18f));

                _stylesInitialized = _monoWrapStyle != null && _selectedBgTexture != null;
            }
            catch
            {
                // Never throw from style initialization; retry later.
                _stylesInitialized = false;
            }
        }

        private static Texture2D MakeTex(int width, int height, Color color)
        {
            Texture2D tex = new(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

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
    }
}
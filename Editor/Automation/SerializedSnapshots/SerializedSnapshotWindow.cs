using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public sealed class SerializedSnapshotWindow : EditorWindow
    {
        private const string WindowTitle = "Serialized Snapshots";
        private const float SidebarWidth = 360f;

        private Vector2 _snapshotListScroll;
        private Vector2 _detailsScroll;
        private string _selectedSnapshotId;
        private string _renameSnapshotId;
        private string _renameBuffer = string.Empty;
        private int _selectedComponentIndex;
        private string _snapshotSearch = string.Empty;
        private string _propertySearch = string.Empty;
        private readonly Dictionary<string, bool> _componentPropertyFoldouts = new();

        private static GUIStyle _headerTitleStyle;
        private static GUIStyle _headerSubtitleStyle;
        private static GUIStyle _panelStyle;
        private static GUIStyle _cardStyle;
        private static GUIStyle _selectedCardStyle;
        private static GUIStyle _sectionTitleStyle;
        private static GUIStyle _mutedLabelStyle;
        private static GUIStyle _metricValueStyle;
        private static GUIStyle _metricCaptionStyle;
        private static GUIStyle _chipStyle;
        private static GUIStyle _chipAccentStyle;
        private static GUIStyle _primaryButtonStyle;
        private static GUIStyle _dangerButtonStyle;
        private static GUIStyle _propertyPathStyle;
        private static GUIStyle _propertyValueStyle;
        private static GUIStyle _searchFieldStyle;

        private static readonly Color HeaderColor = new(0.18f, 0.53f, 0.96f, 0.16f);
        private static readonly Color PanelBorderColor = new(1f, 1f, 1f, 0.06f);
        private static readonly Color SelectionColor = new(0.23f, 0.56f, 0.95f, 0.20f);
        private static readonly Color RowColor = new(1f, 1f, 1f, 0.025f);
        private static readonly Color RowAltColor = new(1f, 1f, 1f, 0.045f);
        private static readonly Color AccentTextColor = new(0.45f, 0.75f, 1f, 1f);

        [MenuItem("Tools/LegendaryTools/Automation/Serialized Snapshot Library")]
        private static void Open()
        {
            SerializedSnapshotWindow window = GetWindow<SerializedSnapshotWindow>(WindowTitle);
            window.minSize = new Vector2(1180f, 640f);
            window.Show();
        }

        [MenuItem("CONTEXT/Component/Capture Serialized Snapshot")]
        private static void CaptureComponentSnapshot(MenuCommand command)
        {
            if (command.context is not Component component)
                return;

            SerializedSnapshotRecord snapshot = SerializedSnapshotService.CaptureComponent(component);
            Debug.Log($"Captured snapshot '{snapshot.Name}'.", component);
            Open();
        }

        [MenuItem("GameObject/Capture Serialized Snapshot", false, 0)]
        private static void CaptureGameObjectSnapshot(MenuCommand command)
        {
            GameObject gameObject = command.context as GameObject;
            if (gameObject == null)
                gameObject = Selection.activeGameObject;

            if (gameObject == null)
                return;

            SerializedSnapshotRecord snapshot = SerializedSnapshotService.CaptureGameObject(gameObject);
            Debug.Log($"Captured snapshot '{snapshot.Name}'.", gameObject);
            Open();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
            if (string.IsNullOrWhiteSpace(_selectedSnapshotId))
                _selectedSnapshotId = SerializedSnapshotLibrary.instance.Snapshots.FirstOrDefault()?.Id;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawWindowBackground();
            DrawHeader();
            DrawToolbar();

            Rect contentRect = EditorGUILayout.BeginVertical();
            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSnapshotListPanel();
                GUILayout.Space(10f);
                DrawDetailsPanel();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawWindowBackground()
        {
            Rect rect = new(0f, 0f, position.width, position.height);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(0.11f, 0.12f, 0.14f, 1f)
                : new Color(0.92f, 0.93f, 0.95f, 1f));
        }

        private void DrawHeader()
        {
            Rect rect = GUILayoutUtility.GetRect(10f, 72f, GUILayout.ExpandWidth(true));
            DrawRoundedPanel(rect, HeaderColor, 10f);

            Rect innerRect = new(rect.x + 16f, rect.y + 10f, rect.width - 32f, rect.height - 20f);

            GUI.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 26f), WindowTitle, _headerTitleStyle);

            string subtitle = TryGetSelectedGameObject(out GameObject selectedGameObject)
                ? $"Selection: {selectedGameObject.name}"
                : "Selection: none";

            GUI.Label(new Rect(innerRect.x, innerRect.y + 28f, innerRect.width * 0.6f, 18f), subtitle,
                _headerSubtitleStyle);

            string summary = $"{SerializedSnapshotLibrary.instance.Snapshots.Count} snapshots";
            Vector2 size = _chipAccentStyle.CalcSize(new GUIContent(summary));
            Rect chipRect = new(innerRect.xMax - size.x - 18f, innerRect.y + 2f, size.x + 18f, 22f);
            DrawChip(chipRect, summary, _chipAccentStyle);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.VerticalScope(_panelStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = TryGetSelectedGameObject(out GameObject selectedGameObject);

                    if (GUILayout.Button("Capture GameObject", _primaryButtonStyle, GUILayout.Width(156f),
                            GUILayout.Height(26f)))
                    {
                        SerializedSnapshotRecord snapshot = SerializedSnapshotService.CaptureGameObject(selectedGameObject);
                        SelectSnapshot(snapshot);
                    }

                    Component[] components = selectedGameObject != null
                        ? selectedGameObject.GetComponents<Component>().Where(component => component != null).ToArray()
                        : Array.Empty<Component>();

                    GUI.enabled = components.Length > 0;
                    string[] componentLabels = components.Select(component => component.GetType().Name).ToArray();
                    _selectedComponentIndex = Mathf.Clamp(_selectedComponentIndex, 0,
                        Math.Max(componentLabels.Length - 1, 0));
                    _selectedComponentIndex = EditorGUILayout.Popup(_selectedComponentIndex, componentLabels,
                        GUILayout.Width(210f), GUILayout.Height(22f));

                    if (GUILayout.Button("Capture Component", _primaryButtonStyle, GUILayout.Width(146f),
                            GUILayout.Height(26f)))
                    {
                        SerializedSnapshotRecord snapshot =
                            SerializedSnapshotService.CaptureComponent(components[_selectedComponentIndex]);
                        SelectSnapshot(snapshot);
                    }

                    GUI.enabled = true;

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Refresh", GUILayout.Width(82f), GUILayout.Height(24f)))
                        Repaint();
                }
            }
        }

        private void DrawSnapshotListPanel()
        {
            using (new EditorGUILayout.VerticalScope(_panelStyle, GUILayout.Width(SidebarWidth)))
            {
                DrawPanelHeader("Library", "Browse, search and reuse saved captures.");
                EditorGUILayout.Space(6f);

                _snapshotSearch = EditorGUILayout.TextField(_snapshotSearch, _searchFieldStyle, GUILayout.Height(22f));
                EditorGUILayout.Space(6f);

                IReadOnlyList<SerializedSnapshotRecord> snapshots = SerializedSnapshotLibrary.instance.Snapshots;
                List<SerializedSnapshotRecord> filteredSnapshots = snapshots
                    .Where(snapshot => snapshot != null && MatchesSnapshotFilter(snapshot))
                    .ToList();

                _snapshotListScroll = EditorGUILayout.BeginScrollView(_snapshotListScroll, GUIStyle.none);

                if (filteredSnapshots.Count == 0)
                {
                    DrawEmptyState(
                        string.IsNullOrWhiteSpace(_snapshotSearch)
                            ? "No snapshots captured yet."
                            : "No snapshots match the current search.");
                }
                else
                {
                    foreach (SerializedSnapshotRecord snapshot in filteredSnapshots)
                        DrawSnapshotCard(snapshot);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSnapshotCard(SerializedSnapshotRecord snapshot)
        {
            bool isSelected = snapshot.Id == _selectedSnapshotId;
            GUIStyle style = isSelected ? _selectedCardStyle : _cardStyle;

            Rect cardRect = EditorGUILayout.BeginVertical(style);
            try
            {
                EditorGUILayout.LabelField(snapshot.Name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(snapshot.Scope.ToString(), _mutedLabelStyle);
                EditorGUILayout.LabelField(snapshot.SourceHierarchyPath, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawMiniChip($"{snapshot.Components.Count} comps");
                    DrawMiniChip($"{snapshot.UnsupportedReferenceCount} unsupported");
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        snapshot.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                        _mutedLabelStyle, GUILayout.Width(136f));
                }
            }
            finally
            {
                EditorGUILayout.EndVertical();
            }

            if (Event.current.type == EventType.MouseDown && cardRect.Contains(Event.current.mousePosition))
            {
                SelectSnapshot(snapshot);
                Event.current.Use();
            }

            GUILayout.Space(6f);
        }

        private void DrawDetailsPanel()
        {
            SerializedSnapshotRecord snapshot = SerializedSnapshotLibrary.instance.Get(_selectedSnapshotId);

            using (new EditorGUILayout.VerticalScope(_panelStyle))
            {
                if (snapshot == null)
                {
                    DrawPanelHeader("Details", "Select or capture a snapshot to inspect it.");
                    EditorGUILayout.Space(10f);
                    DrawEmptyState("No snapshot selected.");
                    return;
                }

                _detailsScroll = EditorGUILayout.BeginScrollView(_detailsScroll, GUIStyle.none);

                DrawSnapshotHeader(snapshot);
                EditorGUILayout.Space(10f);
                DrawSnapshotMetadata(snapshot);
                EditorGUILayout.Space(10f);
                DrawPreview(snapshot);
                EditorGUILayout.Space(10f);
                DrawComponentBreakdown(snapshot);

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSnapshotHeader(SerializedSnapshotRecord snapshot)
        {
            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                DrawPanelHeader("Selected Snapshot", snapshot.Name);
                EditorGUILayout.Space(6f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (_renameSnapshotId != snapshot.Id)
                    {
                        _renameSnapshotId = snapshot.Id;
                        _renameBuffer = snapshot.Name;
                    }

                    _renameBuffer = EditorGUILayout.TextField("Name", _renameBuffer);

                    if (GUILayout.Button("Rename", GUILayout.Width(78f), GUILayout.Height(24f)))
                    {
                        SerializedSnapshotLibrary.instance.Rename(snapshot.Id, _renameBuffer);
                        _renameBuffer = snapshot.Name;
                    }

                    if (GUILayout.Button("Duplicate", GUILayout.Width(84f), GUILayout.Height(24f)))
                    {
                        SerializedSnapshotRecord duplicate = SerializedSnapshotLibrary.instance.Duplicate(snapshot.Id);
                        SelectSnapshot(duplicate);
                    }

                    if (GUILayout.Button("Delete", _dangerButtonStyle, GUILayout.Width(72f), GUILayout.Height(24f)))
                    {
                        SerializedSnapshotLibrary.instance.Delete(snapshot.Id);
                        _selectedSnapshotId = SerializedSnapshotLibrary.instance.Snapshots.FirstOrDefault()?.Id;
                        _renameSnapshotId = _selectedSnapshotId;
                        _renameBuffer = string.Empty;
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void DrawSnapshotMetadata(SerializedSnapshotRecord snapshot)
        {
            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                DrawPanelHeader("Source", "Origin and composition of this capture.");
                EditorGUILayout.Space(8f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawMetricCard("Scope", snapshot.Scope.ToString());
                    DrawMetricCard("Components", snapshot.Components.Count.ToString());
                    DrawMetricCard("Unsupported", snapshot.UnsupportedReferenceCount.ToString());
                }

                EditorGUILayout.Space(6f);
                DrawDetailRow("Scene", string.IsNullOrWhiteSpace(snapshot.SourceScenePath)
                    ? "<unsaved scene>"
                    : snapshot.SourceScenePath);
                DrawDetailRow("Hierarchy", snapshot.SourceHierarchyPath);
                if (!string.IsNullOrWhiteSpace(snapshot.SourcePrefabAssetPath))
                    DrawDetailRow("Prefab", snapshot.SourcePrefabAssetPath);
            }
        }

        private void DrawPreview(SerializedSnapshotRecord snapshot)
        {
            TryGetSelectedGameObject(out GameObject selectedGameObject);
            SerializedSnapshotPreview preview =
                SerializedSnapshotService.BuildPreview(snapshot, selectedGameObject);

            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                DrawPanelHeader("Preview", "Compatibility against the current selection.");
                EditorGUILayout.Space(8f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawMetricCard("Target", preview.TargetRoot != null ? preview.TargetRoot.name : "<none>");
                    DrawMetricCard("Matched", preview.MatchedComponents.ToString());
                    DrawMetricCard("Missing", preview.MissingComponents.ToString());
                    DrawMetricCard("Resolvable", preview.ResolvableReferences.ToString());
                    DrawMetricCard("Unresolvable", preview.UnresolvableReferences.ToString());
                }

                if (preview.Messages.Count > 0)
                {
                    EditorGUILayout.Space(6f);
                    foreach (string message in preview.Messages.Take(6))
                        DrawMessageRow(message);
                }

                EditorGUILayout.Space(8f);
                GUI.enabled = preview.HasTarget;
                if (GUILayout.Button("Apply To Selection", _primaryButtonStyle, GUILayout.Height(28f)))
                {
                    SerializedSnapshotApplyReport report =
                        SerializedSnapshotService.ApplyToGameObject(snapshot, preview.TargetRoot);

                    if (report.HasWarnings)
                    {
                        Debug.LogWarning(
                            $"Applied snapshot '{snapshot.Name}' with warnings.\n{string.Join("\n", report.Messages)}",
                            preview.TargetRoot);
                    }
                    else
                    {
                        Debug.Log($"Applied snapshot '{snapshot.Name}'.", preview.TargetRoot);
                    }
                }

                GUI.enabled = true;
            }
        }

        private void DrawComponentBreakdown(SerializedSnapshotRecord snapshot)
        {
            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                DrawPanelHeader("Captured Fields", "Inspect the exact serialized values stored in this snapshot.");
                EditorGUILayout.Space(6f);
                _propertySearch = EditorGUILayout.TextField(_propertySearch, _searchFieldStyle, GUILayout.Height(22f));
                EditorGUILayout.Space(8f);

                foreach (SerializedComponentSnapshot component in snapshot.Components)
                    DrawComponentCard(snapshot, component);
            }
        }

        private void DrawComponentCard(SerializedSnapshotRecord snapshot, SerializedComponentSnapshot component)
        {
            using (new EditorGUILayout.VerticalScope(_panelStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(component.ComponentDisplayName, _sectionTitleStyle);
                    GUILayout.Space(8f);
                    DrawMiniChip($"#{component.ComponentOccurrenceIndex}");
                    DrawMiniChip($"{component.TopLevelPropertyCount} roots");
                    DrawMiniChip($"{component.ObjectReferenceCount} refs");
                    GUILayout.FlexibleSpace();
                }

                string foldoutKey = $"{snapshot.Id}:{component.ComponentTypeName}:{component.ComponentOccurrenceIndex}";
                bool isExpanded = _componentPropertyFoldouts.TryGetValue(foldoutKey, out bool storedValue) &&
                                  storedValue;
                isExpanded = EditorGUILayout.Foldout(isExpanded,
                    $"Captured Fields ({GetFilteredProperties(component).Count})", true);
                _componentPropertyFoldouts[foldoutKey] = isExpanded;

                if (!isExpanded)
                    return;

                EditorGUILayout.Space(4f);
                List<SerializedCapturedPropertySnapshot> properties = GetFilteredProperties(component);

                if (properties.Count == 0)
                {
                    DrawEmptyState(string.IsNullOrWhiteSpace(_propertySearch)
                        ? "No visible properties captured for this component."
                        : "No properties match the current filter.");
                    return;
                }

                for (int index = 0; index < properties.Count; index++)
                    DrawPropertyRow(properties[index], index);
            }
        }

        private void DrawPropertyRow(SerializedCapturedPropertySnapshot property, int index)
        {
            Rect rowRect = GUILayoutUtility.GetRect(10f, 24f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rowRect, index % 2 == 0 ? RowColor : RowAltColor);

            Rect innerRect = new(rowRect.x + 8f, rowRect.y + 3f, rowRect.width - 16f, rowRect.height - 6f);
            float indent = property.Depth * 14f;
            float pathWidth = Mathf.Clamp(innerRect.width * 0.42f, 240f, 420f);

            GUI.Label(new Rect(innerRect.x + indent, innerRect.y, pathWidth - indent, innerRect.height),
                $"{property.PropertyPath} [{property.PropertyTypeName}]",
                _propertyPathStyle);

            EditorGUI.SelectableLabel(
                new Rect(innerRect.x + pathWidth, innerRect.y, innerRect.width - pathWidth, innerRect.height),
                string.IsNullOrEmpty(property.ValuePreview) ? "<empty>" : property.ValuePreview,
                _propertyValueStyle);
        }

        private void DrawPanelHeader(string title, string subtitle)
        {
            EditorGUILayout.LabelField(title, _sectionTitleStyle);
            if (!string.IsNullOrWhiteSpace(subtitle))
                EditorGUILayout.LabelField(subtitle, _mutedLabelStyle);
        }

        private void DrawMetricCard(string caption, string value)
        {
            using (new EditorGUILayout.VerticalScope(_panelStyle, GUILayout.MinWidth(100f)))
            {
                EditorGUILayout.LabelField(caption, _metricCaptionStyle);
                EditorGUILayout.LabelField(value, _metricValueStyle);
            }
        }

        private void DrawDetailRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, _mutedLabelStyle, GUILayout.Width(80f));
                EditorGUILayout.SelectableLabel(value, _propertyValueStyle, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void DrawMessageRow(string message)
        {
            using (new EditorGUILayout.HorizontalScope(_panelStyle))
            {
                GUILayout.Label(EditorGUIUtility.IconContent("console.infoicon"), GUILayout.Width(18f), GUILayout.Height(18f));
                EditorGUILayout.LabelField(message, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawEmptyState(string message)
        {
            using (new EditorGUILayout.VerticalScope(_panelStyle))
            {
                EditorGUILayout.LabelField(message, _mutedLabelStyle);
            }
        }

        private void DrawMiniChip(string text)
        {
            Vector2 size = _chipStyle.CalcSize(new GUIContent(text));
            Rect rect = GUILayoutUtility.GetRect(size.x + 14f, 18f, GUILayout.Width(size.x + 14f), GUILayout.Height(18f));
            DrawChip(rect, text, _chipStyle);
        }

        private void DrawChip(Rect rect, string text, GUIStyle style)
        {
            EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.06f));
            GUI.Label(rect, text, style);
        }

        private List<SerializedCapturedPropertySnapshot> GetFilteredProperties(SerializedComponentSnapshot component)
        {
            if (string.IsNullOrWhiteSpace(_propertySearch))
                return component.CapturedProperties;

            string filter = _propertySearch.Trim();
            return component.CapturedProperties
                .Where(property =>
                    property.PropertyPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    property.PropertyTypeName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(property.ValuePreview) &&
                     property.ValuePreview.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
        }

        private bool MatchesSnapshotFilter(SerializedSnapshotRecord snapshot)
        {
            if (string.IsNullOrWhiteSpace(_snapshotSearch))
                return true;

            string filter = _snapshotSearch.Trim();
            return snapshot.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   snapshot.SourceHierarchyPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   snapshot.Components.Any(component =>
                       component.ComponentDisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void SelectSnapshot(SerializedSnapshotRecord snapshot)
        {
            if (snapshot == null)
                return;

            _selectedSnapshotId = snapshot.Id;
            _renameSnapshotId = snapshot.Id;
            _renameBuffer = snapshot.Name;
            Repaint();
        }

        private static bool TryGetSelectedGameObject(out GameObject gameObject)
        {
            if (Selection.activeObject is Component selectedComponent)
            {
                gameObject = selectedComponent.gameObject;
                return gameObject != null;
            }

            gameObject = Selection.activeGameObject;
            return gameObject != null;
        }

        private static void DrawRoundedPanel(Rect rect, Color color, float radius)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.Box(rect, GUIContent.none, EditorGUIUtility.isProSkin ? "HelpBox" : "GroupBox");
            GUI.color = previousColor;

            Handles.BeginGUI();
            Handles.color = PanelBorderColor;
            Vector3[] points =
            {
                new(rect.x, rect.y), new(rect.xMax, rect.y), new(rect.xMax, rect.yMax), new(rect.x, rect.yMax), new(rect.x, rect.y)
            };
            Handles.DrawAAPolyLine(1.2f, points);
            Handles.EndGUI();
        }

        private static void EnsureStyles()
        {
            if (_headerTitleStyle != null)
                return;

            _headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                fixedHeight = 24,
                alignment = TextAnchor.MiddleLeft
            };

            _headerSubtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.84f, 0.86f, 0.9f) : new Color(0.28f, 0.31f, 0.36f) }
            };

            _panelStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 0, 0)
            };

            _cardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 0, 0)
            };

            _selectedCardStyle = new GUIStyle(_cardStyle);
            _selectedCardStyle.normal.background = MakeColorTexture(SelectionColor);

            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12
            };

            _mutedLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.72f, 0.75f, 0.79f)
                        : new Color(0.35f, 0.38f, 0.42f)
                }
            };

            _metricValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = AccentTextColor }
            };

            _metricCaptionStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = _mutedLabelStyle.normal.textColor }
            };

            _chipStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 2, 2)
            };

            _chipAccentStyle = new GUIStyle(_chipStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = AccentTextColor }
            };

            _primaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold
            };

            _dangerButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(1f, 0.55f, 0.55f) : new Color(0.72f, 0.18f, 0.18f) }
            };

            _propertyPathStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };

            _propertyValueStyle = new GUIStyle(EditorStyles.textField)
            {
                alignment = TextAnchor.MiddleLeft
            };

            _searchFieldStyle = new GUIStyle("ToolbarSeachTextField");
            if (_searchFieldStyle == null || _searchFieldStyle.normal.background == null)
                _searchFieldStyle = new GUIStyle(EditorStyles.toolbarSearchField);
        }

        private static Texture2D MakeColorTexture(Color color)
        {
            Texture2D texture = new(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }
}

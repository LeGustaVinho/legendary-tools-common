using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public sealed class CsFileTreeWindow : EditorWindow
{
    private const string WindowTitle = "C# File Tree";
    private const float ToolbarHeight = 20f;

    [SerializeField] private TreeViewState<int> _treeViewState;

    private SearchField _searchField;
    private CsFileTreeView _treeView;

    // Filters
    private int _minLines;
    private int _maxLines = 1_000_000;

    private int _minCoveragePercent;
    private int _maxCoveragePercent = 100;

    [MenuItem("Tools/LegendaryTools/Code/Metrics")]
    public static void Open()
    {
        CsFileTreeWindow window = GetWindow<CsFileTreeWindow>();
        window.titleContent = new GUIContent(WindowTitle);
        window.minSize = new Vector2(860f, 380f);
        window.Show();
    }

    private void OnEnable()
    {
        _treeViewState ??= new TreeViewState<int>();

        _searchField ??= new SearchField();
        _searchField.downOrUpArrowKeyPressed += OnSearchFieldArrowKeyPressed;

        _treeView ??= new CsFileTreeView(_treeViewState);
        ApplyFiltersToTree();
        _treeView.Reload();
    }

    private void OnGUI()
    {
        DrawToolbar();

        Rect treeRect = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f);
        _treeView.OnGUI(treeRect);
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight)))
        {
            GUILayout.Label("Search:", GUILayout.Width(45f));

            string newSearch = _searchField.OnToolbarGUI(_treeView.searchString);
            if (newSearch != _treeView.searchString)
            {
                _treeView.searchString = newSearch;
                _treeView.Reload();
            }

            GUILayout.Space(10f);

            // Lines filter
            GUILayout.Label("Lines:", GUILayout.Width(40f));
            int newMinLines = EditorGUILayout.IntField(_minLines, GUILayout.Width(55f));
            GUILayout.Label("-", GUILayout.Width(10f));
            int newMaxLines = EditorGUILayout.IntField(_maxLines, GUILayout.Width(70f));

            GUILayout.Space(10f);

            // Coverage filter
            GUILayout.Label("Coverage %:", GUILayout.Width(78f));
            int newMinCov = EditorGUILayout.IntField(_minCoveragePercent, GUILayout.Width(40f));
            GUILayout.Label("-", GUILayout.Width(10f));
            int newMaxCov = EditorGUILayout.IntField(_maxCoveragePercent, GUILayout.Width(40f));

            bool filtersChanged =
                newMinLines != _minLines ||
                newMaxLines != _maxLines ||
                newMinCov != _minCoveragePercent ||
                newMaxCov != _maxCoveragePercent;

            if (filtersChanged)
            {
                _minLines = Mathf.Max(0, newMinLines);
                _maxLines = Mathf.Clamp(newMaxLines, _minLines, 1_000_000);

                _minCoveragePercent = Mathf.Clamp(newMinCov, 0, 100);
                _maxCoveragePercent = Mathf.Clamp(newMaxCov, _minCoveragePercent, 100);

                ApplyFiltersToTree();
                _treeView.Reload();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Expand All", EditorStyles.toolbarButton, GUILayout.Width(85f))) _treeView.ExpandAll();

            if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                _treeView.CollapseAll();

            if (GUILayout.Button("Reset Filters", EditorStyles.toolbarButton, GUILayout.Width(95f)))
            {
                _minLines = 0;
                _maxLines = 1_000_000;
                _minCoveragePercent = 0;
                _maxCoveragePercent = 100;

                ApplyFiltersToTree();
                _treeView.Reload();
            }

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                _treeView.ClearMetricsCache();
                _treeView.Reload();
            }
        }
    }

    private void ApplyFiltersToTree()
    {
        _treeView.SetNumericFilters(
            _minLines,
            _maxLines,
            _minCoveragePercent,
            _maxCoveragePercent);
    }

    private void OnSearchFieldArrowKeyPressed()
    {
        _treeView.SetFocusAndEnsureSelectedItemVisible();
        Repaint();
    }
}
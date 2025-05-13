using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Editor window for browsing and managing ScriptableObject instances in a Unity project.
    /// Displays a tree view of ScriptableObject types and a table view of their instances.
    /// </summary>
    public class ScriptableObjectBrowser : EditorWindow
    {
        private Vector2 _leftScrollPosition;
        private Vector2 _rightScrollPosition;
        private System.Type _selectedType;
        private TreeNode _rootNode;
        private Dictionary<System.Type, List<Object>> _instancesCache;
        private Dictionary<System.Type, ScriptableObjectTableView> _tableViews;
        private HashSet<string> _expandedNodes = new HashSet<string>();

        /// <summary>
        /// Represents a node in the tree view of ScriptableObject types.
        /// </summary>
        private class TreeNode
        {
            public string Name;
            public string FullPath;
            public List<TreeNode> Children = new List<TreeNode>();
            public List<System.Type> Types = new List<System.Type>();
        }

        /// <summary>
        /// Opens the ScriptableObject Browser window from the Unity Editor menu.
        /// </summary>
        [MenuItem("Tools/LegendaryTools/ScriptableObject/ScriptableObject Browser")]
        public static void ShowWindow()
        {
            GetWindow<ScriptableObjectBrowser>("ScriptableObject Browser");
        }

        /// <summary>
        /// Initializes the window when it is enabled.
        /// </summary>
        private void OnEnable()
        {
            RefreshTree();
            _instancesCache = new Dictionary<System.Type, List<Object>>();
            _tableViews = new Dictionary<System.Type, ScriptableObjectTableView>();
        }

        /// <summary>
        /// Draws the GUI for the ScriptableObject Browser window.
        /// </summary>
        private void OnGUI()
        {
            if (_rootNode == null)
            {
                RefreshTree();
            }

            GUILayout.BeginHorizontal();

            DrawLeftPanel();
            DrawRightPanel();

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Refreshes the tree view by scanning all ScriptableObject types in the project.
        /// Displays a progress bar to indicate processing status.
        /// </summary>
        private void RefreshTree()
        {
            _rootNode = new TreeNode { Name = "Root", FullPath = "" };
            var scriptableObjectType = typeof(ScriptableObject);

            // Retrieve all non-abstract ScriptableObject types in the project
            var types = System.AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => scriptableObjectType.IsAssignableFrom(type) && !type.IsAbstract)
                .ToList(); // Materialize the list to count total types

            int totalTypes = types.Count;
            int currentTypeIndex = 0;

            foreach (var type in types)
            {
                // Update the progress bar
                string typeName = type.Name;
                float progress = (float)(currentTypeIndex + 1) / totalTypes;
                EditorUtility.DisplayProgressBar(
                    "Refreshing ScriptableObject Browser",
                    $"Processing type: {typeName}",
                    progress
                );

                // Check if the type has instances
                string[] guids = AssetDatabase.FindAssets($"t:{type.Name}");
                if (guids.Length > 0)
                {
                    var attribute = type.GetCustomAttribute<ScriptableObjectInfoAttribute>();
                    string hierarchyPath = attribute?.HierarchyPath ?? "Default";

                    // Split the path into parts
                    var pathParts = hierarchyPath.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToArray();
                    if (pathParts.Length == 0)
                    {
                        pathParts = new[] { "Default" };
                    }

                    // Add the type to the tree
                    AddTypeToTree(_rootNode, pathParts, type, 0);
                }

                currentTypeIndex++;
            }

            // Remove empty nodes
            PruneEmptyNodes(_rootNode);

            // Clear the progress bar
            EditorUtility.ClearProgressBar();
        }

        /// <summary>
        /// Adds a ScriptableObject type to the tree structure based on its hierarchy path.
        /// </summary>
        /// <param name="parent">The parent node to add the type to.</param>
        /// <param name="pathParts">The parts of the hierarchy path.</param>
        /// <param name="type">The ScriptableObject type to add.</param>
        /// <param name="depth">The current depth in the path.</param>
        private void AddTypeToTree(TreeNode parent, string[] pathParts, System.Type type, int depth)
        {
            if (depth == pathParts.Length)
            {
                parent.Types.Add(type);
                return;
            }

            string currentPart = pathParts[depth];
            string currentPath = string.IsNullOrEmpty(parent.FullPath) ? currentPart : $"{parent.FullPath}/{currentPart}";

            var childNode = parent.Children.Find(n => n.Name == currentPart);
            if (childNode == null)
            {
                childNode = new TreeNode { Name = currentPart, FullPath = currentPath };
                parent.Children.Add(childNode);
            }

            AddTypeToTree(childNode, pathParts, type, depth + 1);
        }

        /// <summary>
        /// Removes empty nodes from the tree structure.
        /// </summary>
        /// <param name="node">The node to prune.</param>
        /// <returns>True if the node has types or children; otherwise, false.</returns>
        private bool PruneEmptyNodes(TreeNode node)
        {
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                if (!PruneEmptyNodes(node.Children[i]))
                {
                    node.Children.RemoveAt(i);
                }
            }

            return node.Types.Count > 0 || node.Children.Count > 0;
        }

        /// <summary>
        /// Draws the left panel containing the tree view of ScriptableObject types.
        /// </summary>
        private void DrawLeftPanel()
        {
            GUILayout.BeginVertical(GUILayout.Width(250));
            _leftScrollPosition = GUILayout.BeginScrollView(_leftScrollPosition);

            DrawTreeNode(_rootNode, 0);

            GUILayout.EndScrollView();
            if (GUILayout.Button("Refresh"))
            {
                RefreshTree();
                _instancesCache.Clear();
                _tableViews.Clear();
                _expandedNodes.Clear();
            }
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Draws a node in the tree view, including its children and types.
        /// </summary>
        /// <param name="node">The node to draw.</param>
        /// <param name="depth">The indentation depth.</param>
        private void DrawTreeNode(TreeNode node, int depth)
        {
            if (node == _rootNode)
            {
                foreach (var child in node.Children.OrderBy(c => c.Name))
                {
                    DrawTreeNode(child, depth);
                }
                return;
            }

            EditorGUI.indentLevel = depth;

            bool isExpanded = _expandedNodes.Contains(node.FullPath);
            bool newExpanded = EditorGUILayout.Foldout(isExpanded, node.Name, true);

            if (newExpanded != isExpanded)
            {
                if (newExpanded)
                    _expandedNodes.Add(node.FullPath);
                else
                    _expandedNodes.Remove(node.FullPath);
            }

            if (isExpanded)
            {
                foreach (var type in node.Types.OrderBy(t => t.Name))
                {
                    var attribute = type.GetCustomAttribute<ScriptableObjectInfoAttribute>();
                    string displayName = attribute?.DisplayName ?? type.Name;

                    GUIStyle style = new GUIStyle(EditorStyles.label);
                    style.padding = new RectOffset(20, 0, 0, 0);
                    if (type == _selectedType)
                    {
                        style.normal.textColor = Color.cyan;
                    }

                    if (GUILayout.Button(displayName, style))
                    {
                        _selectedType = type;
                        LoadInstances(type);
                        if (!_tableViews.ContainsKey(_selectedType))
                        {
                            _tableViews[_selectedType] = new ScriptableObjectTableView();
                        }

                        _tableViews[_selectedType].SetScriptableObjects(_instancesCache[_selectedType].Cast<ScriptableObject>().ToList());
                    }

                    if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                    {
                        string description = attribute?.Description ?? "No description provided.";
                        GUI.Label(new Rect(Event.current.mousePosition.x + 10, Event.current.mousePosition.y, 300, 50), description, EditorStyles.helpBox);
                    }
                }

                foreach (var child in node.Children.OrderBy(c => c.Name))
                {
                    DrawTreeNode(child, depth + 1);
                }
            }

            EditorGUI.indentLevel = 0;
        }

        /// <summary>
        /// Draws the right panel containing the table view of ScriptableObject instances.
        /// </summary>
        private void DrawRightPanel()
        {
            GUILayout.BeginVertical();
            _rightScrollPosition = GUILayout.BeginScrollView(_rightScrollPosition);

            if (_selectedType != null)
            {
                var attribute = _selectedType.GetCustomAttribute<ScriptableObjectInfoAttribute>();
                string displayName = attribute?.DisplayName ?? _selectedType.Name;
                EditorGUILayout.LabelField($"Instances of {displayName}", EditorStyles.boldLabel);

                if (_instancesCache.ContainsKey(_selectedType) && _instancesCache[_selectedType].Count > 0)
                {
                    _tableViews[_selectedType].DrawTable();
                }
                else
                {
                    EditorGUILayout.LabelField("No instances found.");
                }
            }
            else
            {
                EditorGUILayout.LabelField("Select a ScriptableObject type from the left panel.");
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Loads all instances of a given ScriptableObject type from the project.
        /// </summary>
        /// <param name="type">The ScriptableObject type to load instances for.</param>
        private void LoadInstances(System.Type type)
        {
            if (!_instancesCache.ContainsKey(type))
            {
                string[] guids = AssetDatabase.FindAssets($"t:{type.Name}");
                List<Object> instances = new List<Object>();

                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                    if (asset != null && asset.GetType() == type)
                    {
                        instances.Add(asset);
                    }
                }

                _instancesCache[type] = instances;
            }
        }
    }
}

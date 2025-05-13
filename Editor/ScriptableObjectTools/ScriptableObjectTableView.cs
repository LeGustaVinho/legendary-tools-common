using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// A class that renders a table view for editing multiple ScriptableObject instances in the Unity Editor.
    /// Implements ISerializationCallbackReceiver to handle serialization of table data.
    /// </summary>
    public class ScriptableObjectTableView : ISerializationCallbackReceiver
    {
        private readonly List<ScriptableObject> _scriptableObjects = new List<ScriptableObject>();
        private Vector2 _scrollPosition;
        private readonly Dictionary<string, float> _columnWidths = new Dictionary<string, float>();
        private readonly Dictionary<ScriptableObject, float> _rowHeights = new Dictionary<ScriptableObject, float>();
        private bool _isResizingColumn;
        private bool _isResizingRow;
        private string _resizingColumn;
        private ScriptableObject _resizingRow;
        private readonly float _minColumnWidth = 50f;
        private readonly float _minRowHeight = 20f;
        private readonly List<string> _fieldNames = new List<string>();
        private readonly Color _handlerLineColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        [SerializeField] private List<ScriptableObject> _serializedScriptableObjects = new List<ScriptableObject>();
        [SerializeField] private List<string> _serializedColumnKeys = new List<string>();
        [SerializeField] private List<float> _serializedColumnWidths = new List<float>();
        [SerializeField] private List<ScriptableObject> _serializedRowKeys = new List<ScriptableObject>();
        [SerializeField] private List<float> _serializedRowHeights = new List<float>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptableObjectTableView"/> class.
        /// Sets the default width for the "Object" column.
        /// </summary>
        public ScriptableObjectTableView()
        {
            _columnWidths["Object"] = 150f;
        }

        /// <summary>
        /// Sets the list of ScriptableObjects to be displayed in the table and updates field names and column widths.
        /// </summary>
        /// <param name="objects">The list of ScriptableObjects to display.</param>
        public void SetScriptableObjects(List<ScriptableObject> objects)
        {
            _scriptableObjects.Clear();
            _scriptableObjects.AddRange(objects);

            foreach (var obj in _scriptableObjects)
            {
                if (obj != null && !_rowHeights.ContainsKey(obj))
                {
                    _rowHeights[obj] = EditorGUIUtility.singleLineHeight;
                }
            }

            var keysToRemove = _rowHeights.Keys.Where(k => !_scriptableObjects.Contains(k)).ToList();
            foreach (var key in keysToRemove)
            {
                _rowHeights.Remove(key);
            }

            UpdateFieldNamesAndColumnWidths();
        }

        /// <summary>
        /// Serializes the table data before saving.
        /// </summary>
        public void OnBeforeSerialize()
        {
            _serializedScriptableObjects.Clear();
            _serializedScriptableObjects.AddRange(_scriptableObjects);

            _serializedColumnKeys.Clear();
            _serializedColumnWidths.Clear();
            foreach (var pair in _columnWidths)
            {
                _serializedColumnKeys.Add(pair.Key);
                _serializedColumnWidths.Add(pair.Value);
            }

            _serializedRowKeys.Clear();
            _serializedRowHeights.Clear();
            foreach (var pair in _rowHeights)
            {
                _serializedRowKeys.Add(pair.Key);
                _serializedRowHeights.Add(pair.Value);
            }
        }

        /// <summary>
        /// Deserializes the table data after loading and restores the table state.
        /// </summary>
        public void OnAfterDeserialize()
        {
            _scriptableObjects.Clear();
            _scriptableObjects.AddRange(_serializedScriptableObjects);

            _columnWidths.Clear();
            _columnWidths["Object"] = 150f;
            for (int i = 0; i < System.Math.Min(_serializedColumnKeys.Count, _serializedColumnWidths.Count); i++)
            {
                _columnWidths[_serializedColumnKeys[i]] = _serializedColumnWidths[i];
            }

            _rowHeights.Clear();
            for (int i = 0; i < System.Math.Min(_serializedRowKeys.Count, _serializedRowHeights.Count); i++)
            {
                if (_serializedRowKeys[i] != null)
                {
                    _rowHeights[_serializedRowKeys[i]] = _serializedRowHeights[i];
                }
            }

            foreach (var obj in _scriptableObjects)
            {
                if (obj != null && !_rowHeights.ContainsKey(obj))
                {
                    _rowHeights[obj] = EditorGUIUtility.singleLineHeight;
                }
            }
        }

        /// <summary>
        /// Draws the table UI, including headers, content, and an "Add" button.
        /// </summary>
        public void DrawTable()
        {
            if (_scriptableObjects.Count == 0)
            {
                EditorGUILayout.HelpBox("No ScriptableObjects to display.", MessageType.Info);
                return;
            }

            if (!ValidateTypes())
            {
                EditorGUILayout.HelpBox("All ScriptableObjects must be of the same type.", MessageType.Error);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawTableHeader();
            DrawTableContent();
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Add", GUILayout.Width(100)))
            {
                AddNewScriptableObject();
            }

            HandleColumnResizing();
            HandleRowResizing();

            if (GUI.changed)
            {
                SaveAllChanges();
            }
        }

        /// <summary>
        /// Updates the list of field names and column widths based on the first ScriptableObject's serialized properties.
        /// </summary>
        private void UpdateFieldNamesAndColumnWidths()
        {
            _fieldNames.Clear();
            if (_scriptableObjects.Count > 0 && _scriptableObjects[0] != null)
            {
                var serializedObject = new SerializedObject(_scriptableObjects[0]);
                var iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    if (iterator.propertyPath != "m_Script")
                    {
                        _fieldNames.Add(iterator.name);
                        if (!_columnWidths.ContainsKey(iterator.name))
                        {
                            _columnWidths[iterator.name] = 150f;
                        }
                    }
                    enterChildren = false;
                }
            }
            else
            {
                var objectWidth = _columnWidths["Object"];
                _columnWidths.Clear();
                _columnWidths["Object"] = objectWidth;
            }
        }

        /// <summary>
        /// Validates that all ScriptableObjects in the list are of the same type.
        /// </summary>
        /// <returns>True if all objects are of the same type, false otherwise.</returns>
        private bool ValidateTypes()
        {
            if (_scriptableObjects.Count == 0)
            {
                return false;
            }

            var baseType = _scriptableObjects[0]?.GetType();
            return baseType != null && _scriptableObjects.All(obj => obj != null && obj.GetType() == baseType);
        }

        /// <summary>
        /// Draws the table header with column labels and resize handles.
        /// </summary>
        private void DrawTableHeader()
        {
            EditorGUILayout.BeginHorizontal();
            DrawColumnHeader("Object", "Object");

            foreach (var fieldName in _fieldNames)
            {
                DrawColumnHeader(fieldName, fieldName);
            }

            DrawColumnHeader("", "RemoveButton");
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws a single column header with a label and optional resize handle.
        /// </summary>
        /// <param name="label">The label to display.</param>
        /// <param name="columnKey">The key identifying the column.</param>
        private void DrawColumnHeader(string label, string columnKey)
        {
            float width = columnKey == "RemoveButton" ? 30f : _columnWidths[columnKey];
            Rect headerRect = GUILayoutUtility.GetRect(width, EditorGUIUtility.singleLineHeight, GUILayout.Width(width));
            EditorGUI.LabelField(headerRect, label);

            if (columnKey != "RemoveButton")
            {
                Rect resizeHandleRect = new Rect(headerRect.xMax - 5, headerRect.y, 10, headerRect.height);
                EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeHorizontal);

                Rect lineRect = new Rect(headerRect.xMax - 1, headerRect.y, 1, headerRect.height);
                EditorGUI.DrawRect(lineRect, _handlerLineColor);

                if (Event.current.type == EventType.MouseDown && resizeHandleRect.Contains(Event.current.mousePosition))
                {
                    _isResizingColumn = true;
                    _resizingColumn = columnKey;
                    Event.current.Use();
                }
            }
        }

        /// <summary>
        /// Draws the table content, including rows for each ScriptableObject and their properties.
        /// </summary>
        private void DrawTableContent()
        {
            for (int i = 0; i < _scriptableObjects.Count; i++)
            {
                var obj = _scriptableObjects[i];
                if (obj == null)
                {
                    continue;
                }

                if (!_rowHeights.ContainsKey(obj))
                {
                    _rowHeights[obj] = EditorGUIUtility.singleLineHeight;
                }

                Rect rowRect = GUILayoutUtility.GetRect(0f, _rowHeights[obj], GUILayout.ExpandWidth(true));
                EditorGUILayout.BeginHorizontal();

                var newObj = EditorGUILayout.ObjectField(obj, typeof(ScriptableObject), false, 
                    GUILayout.Width(_columnWidths["Object"])) as ScriptableObject;
                if (newObj != obj)
                {
                    if (newObj == null || (_scriptableObjects.Count == 1 && newObj.GetType() == obj.GetType()) ||
                        (_scriptableObjects.Any(o => o != null && o != obj) && 
                         newObj.GetType() == _scriptableObjects.First(o => o != null && o != obj).GetType()))
                    {
                        _scriptableObjects[i] = newObj;
                        if (newObj != null)
                        {
                            _rowHeights[newObj] = _rowHeights[obj];
                        }
                        _rowHeights.Remove(obj);
                        UpdateFieldNamesAndColumnWidths();
                    }
                }

                var serializedObject = new SerializedObject(obj);
                serializedObject.Update();
                foreach (var fieldName in _fieldNames)
                {
                    var property = serializedObject.FindProperty(fieldName);
                    if (property != null)
                    {
                        EditorGUILayout.PropertyField(property, GUIContent.none, true, 
                            GUILayout.Width(_columnWidths[fieldName]), GUILayout.Height(_rowHeights[obj]));
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Missing", 
                            GUILayout.Width(_columnWidths[fieldName]), GUILayout.Height(_rowHeights[obj]));
                    }
                }
                serializedObject.ApplyModifiedProperties();

                if (GUILayout.Button("X", GUILayout.Width(30), GUILayout.Height(_rowHeights[obj])))
                {
                    _scriptableObjects.RemoveAt(i);
                    _rowHeights.Remove(obj);
                    UpdateFieldNamesAndColumnWidths();
                    i--;
                }

                EditorGUILayout.EndHorizontal();

                Rect resizeHandleRect = new Rect(rowRect.x, rowRect.yMax - 5, rowRect.width, 10);
                EditorGUIUtility.AddCursorRect(resizeHandleRect, MouseCursor.ResizeVertical);

                Rect lineRect = new Rect(rowRect.x, rowRect.yMax - 1, rowRect.width, 1);
                EditorGUI.DrawRect(lineRect, _handlerLineColor);

                if (Event.current.type == EventType.MouseDown && resizeHandleRect.Contains(Event.current.mousePosition))
                {
                    _isResizingRow = true;
                    _resizingRow = obj;
                    Event.current.Use();
                }
            }
        }

        /// <summary>
        /// Handles column resizing based on mouse drag events.
        /// </summary>
        private void HandleColumnResizing()
        {
            if (_isResizingColumn && Event.current.type == EventType.MouseDrag)
            {
                _columnWidths[_resizingColumn] = Mathf.Max(_minColumnWidth, 
                    _columnWidths[_resizingColumn] + Event.current.delta.x);
                Event.current.Use();
            }

            if (Event.current.type == EventType.MouseUp)
            {
                _isResizingColumn = false;
                _resizingColumn = null;
                Event.current.Use();
            }
        }

        /// <summary>
        /// Handles row resizing based on mouse drag events.
        /// </summary>
        private void HandleRowResizing()
        {
            if (_isResizingRow && Event.current.type == EventType.MouseDrag)
            {
                _rowHeights[_resizingRow] = Mathf.Max(_minRowHeight, 
                    _rowHeights[_resizingRow] + Event.current.delta.y);
                Event.current.Use();
            }

            if (Event.current.type == EventType.MouseUp)
            {
                _isResizingRow = false;
                _resizingRow = null;
                Event.current.Use();
            }
        }

        /// <summary>
        /// Saves changes to all ScriptableObjects and refreshes the asset database.
        /// </summary>
        private void SaveAllChanges()
        {
            foreach (var obj in _scriptableObjects)
            {
                if (obj != null)
                {
                    EditorUtility.SetDirty(obj);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Creates and adds a new ScriptableObject of the same type as existing objects to the table.
        /// </summary>
        private void AddNewScriptableObject()
        {
            var baseType = _scriptableObjects.FirstOrDefault(obj => obj != null)?.GetType();
            if (baseType == null)
            {
                Debug.LogWarning("Cannot add new ScriptableObject: No valid type found in the table.");
                return;
            }

            var newInstance = ScriptableObject.CreateInstance(baseType);
            if (newInstance == null)
            {
                Debug.LogError($"Failed to create instance of type {baseType.Name}.");
                return;
            }

            string folderPath = "Assets";
            Object selectedObject = Selection.activeObject;
            if (selectedObject != null)
            {
                string selectedPath = AssetDatabase.GetAssetPath(selectedObject);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    if (AssetDatabase.IsValidFolder(selectedPath))
                    {
                        folderPath = selectedPath;
                    }
                    else
                    {
                        folderPath = System.IO.Path.GetDirectoryName(selectedPath);
                    }
                }
            }

            string assetName = $"New{baseType.Name}.asset";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{assetName}");

            AssetDatabase.CreateAsset(newInstance, assetPath);
            EditorUtility.SetDirty(newInstance);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _scriptableObjects.Add(newInstance);
            _rowHeights[newInstance] = EditorGUIUtility.singleLineHeight;
            UpdateFieldNamesAndColumnWidths();
        }
    }
}

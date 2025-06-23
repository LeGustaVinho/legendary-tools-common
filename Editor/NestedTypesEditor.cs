using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    [CustomEditor(typeof(NestedTypes))]
    public class NestedTypesEditor : UnityEditor.Editor
    {
        private NestedTypes nestedTypes;
        private Dictionary<NestedType, bool> foldoutStates = new Dictionary<NestedType, bool>();
        private Dictionary<NestedType, bool> editStates = new Dictionary<NestedType, bool>();
        private Dictionary<NestedType, string> tempDisplayNames = new Dictionary<NestedType, string>();

        private void OnEnable()
        {
            nestedTypes = (NestedTypes)target;
            UpdateNestedTypesList();
            InitializeFoldouts();
            InitializeEditStates();
        }

        private void UpdateNestedTypesList()
        {
            Undo.RecordObject(nestedTypes, "Update NestedTypes List");
            serializedObject.Update();

            // Get all NestedType assets in the project
            string[] guids = AssetDatabase.FindAssets("t:NestedType");
            List<NestedType> foundTypes = new List<NestedType>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                NestedType nestedType = AssetDatabase.LoadAssetAtPath<NestedType>(path);
                if (nestedType != null && nestedType.Container == nestedTypes)
                {
                    foundTypes.Add(nestedType);
                }
            }

            // Update the nestedTypes list via SerializedProperty
            SerializedProperty nestedTypesProp = serializedObject.FindProperty("nestedTypes");
            nestedTypesProp.ClearArray();

            // Add found types to the array, ensuring no duplicates by ID
            HashSet<Guid> addedIds = new HashSet<Guid>();
            int index = 0;
            foreach (NestedType type in foundTypes)
            {
                if (type != null && !addedIds.Contains(type.Id))
                {
                    nestedTypesProp.InsertArrayElementAtIndex(index);
                    nestedTypesProp.GetArrayElementAtIndex(index).objectReferenceValue = type;
                    addedIds.Add(type.Id);
                    index++;
                }
            }

            serializedObject.ApplyModifiedProperties();
            nestedTypes.InvalidateCache();
            EditorUtility.SetDirty(nestedTypes);
        }

        private void InitializeFoldouts()
        {
            foreach (var type in nestedTypes.AllNestedTypes)
            {
                if (!foldoutStates.ContainsKey(type))
                    foldoutStates[type] = true;
            }
        }

        private void InitializeEditStates()
        {
            foreach (var type in nestedTypes.AllNestedTypes)
            {
                if (!editStates.ContainsKey(type))
                {
                    editStates[type] = false;
                    tempDisplayNames[type] = type.DisplayName;
                }
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            serializedObject.Update();

            EditorGUILayout.LabelField("NestedTypes Hierarchy", EditorStyles.boldLabel);

            // Add New NestedType Button
            if (GUILayout.Button("Add New NestedType"))
            {
                string path = AssetDatabase.GetAssetPath(nestedTypes);
                string folder = System.IO.Path.GetDirectoryName(path);
                NestedType newType = ScriptableObject.CreateInstance<NestedType>();
                newType.DisplayName = "New NestedType";
                AssetDatabase.CreateAsset(newType, AssetDatabase.GenerateUniqueAssetPath($"{folder}/NewNestedType.asset"));
                Undo.RecordObject(nestedTypes, "Add NestedType");
                nestedTypes.AddNestedType(newType);
                AssetDatabase.SaveAssets();
                EditorUtility.SetDirty(nestedTypes);
                InitializeFoldouts();
                InitializeEditStates();
            }

            InitializeFoldouts();
            InitializeEditStates();

            var roots = nestedTypes.AllNestedTypes.Where(t => t != null && t.IsRoot);
            foreach (var root in roots)
            {
                DrawNode(root, 0);
            }

            if (GUILayout.Button("Refresh Hierarchy"))
            {
                nestedTypes.InvalidateCache();
            }

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
                EditorUtility.SetDirty(nestedTypes);
        }

        private void DrawNode(NestedType node, int indent)
        {
            if (node == null) return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent * 15);

            Rect labelRect = GUILayoutUtility.GetRect(new GUIContent(node.DisplayName), EditorStyles.label);
            Rect buttonRect = new Rect(labelRect.xMax - 120, labelRect.y, 30, labelRect.height);
            Rect removeRect = new Rect(labelRect.xMax - 90, labelRect.y, 30, labelRect.height);
            Rect selectRect = new Rect(labelRect.xMax - 60, labelRect.y, 30, labelRect.height);
            Rect clearParentsRect = new Rect(labelRect.xMax - 30, labelRect.y, 30, labelRect.height);
            labelRect.width -= 120;

            // Foldout toggle
            bool hasChildren = nestedTypes.GetChildren(node).Any();
            if (hasChildren)
            {
                foldoutStates[node] = EditorGUI.Foldout(
                    new Rect(labelRect.x, labelRect.y, 12, labelRect.height),
                    foldoutStates[node], GUIContent.none);
                labelRect.x += 12;
                labelRect.width -= 12;
            }

            // Check for multiple parents
            bool hasMultipleParents = node.Parents != null && node.Parents.Length > 1;

            // Edit mode handling
            if (editStates[node])
            {
                GUI.SetNextControlName($"DisplayNameField_{node.GetInstanceID()}");
                if (hasMultipleParents)
                {
                    GUI.color = Color.yellow;
                }
                tempDisplayNames[node] = EditorGUI.TextField(labelRect, tempDisplayNames[node]);
                GUI.color = Color.white;
                if (GUI.Button(buttonRect, "OK") || (Event.current.isKey && Event.current.keyCode == KeyCode.Return &&
                    GUI.GetNameOfFocusedControl() == $"DisplayNameField_{node.GetInstanceID()}"))
                {
                    Undo.RecordObject(node, "Edit DisplayName");
                    node.DisplayName = tempDisplayNames[node];
                    editStates[node] = false;
                    EditorUtility.SetDirty(node);
                }
            }
            else
            {
                // Display depth, leaf status, and multiple parents indication
                string labelText = $"[D:{nestedTypes.GetDepth(node)}]{(node.IsLeafType ? "[L]" : "")} {node.DisplayName}" +
                    (hasMultipleParents ? " [Multi-Parent]" : "");
                if (hasMultipleParents)
                {
                    GUI.color = Color.yellow;
                }
                GUI.Label(labelRect, labelText);
                GUI.color = Color.white;

                // Edit button
                if (GUI.Button(buttonRect, "Edit"))
                {
                    editStates[node] = true;
                    tempDisplayNames[node] = node.DisplayName;
                    GUI.FocusControl($"DisplayNameField_{node.GetInstanceID()}");
                }
            }

            // Select button
            if (GUI.Button(selectRect, "S"))
            {
                EditorGUIUtility.PingObject(node);
                Selection.activeObject = node;
            }

            // Clear Parents button
            if (GUI.Button(clearParentsRect, "C"))
            {
                Undo.RecordObject(node, "Clear NestedType Parents");
                node.Parents = null;
                nestedTypes.InvalidateCache();
                EditorUtility.SetDirty(node);
                AssetDatabase.SaveAssets();
            }

            // Remove button
            if (GUI.Button(removeRect, "X"))
            {
                // Check if node is referenced as a parent
                bool isParent = nestedTypes.AllNestedTypes.Any(t => t != node && t.Parents != null && t.Parents.Contains(node));
                if (!isParent)
                {
                    Undo.RecordObject(nestedTypes, "Remove NestedType");
                    // Find and remove the node from nestedTypes list
                    SerializedProperty nestedTypesProp = serializedObject.FindProperty("nestedTypes");
                    for (int i = 0; i < nestedTypesProp.arraySize; i++)
                    {
                        SerializedProperty element = nestedTypesProp.GetArrayElementAtIndex(i);
                        if (element.objectReferenceValue == node)
                        {
                            nestedTypesProp.DeleteArrayElementAtIndex(i);
                            break;
                        }
                    }
                    foldoutStates.Remove(node);
                    editStates.Remove(node);
                    tempDisplayNames.Remove(node);
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(node));
                    nestedTypes.InvalidateCache();
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(nestedTypes);
                    AssetDatabase.SaveAssets();
                }
                else
                {
                    EditorUtility.DisplayDialog("Cannot Remove", "This NestedType is referenced as a parent by other nodes.", "OK");
                }
            }

            // Handle drag-and-drop target
            if ((Event.current.type == EventType.DragUpdated || Event.current.type == EventType.DragPerform)
                && labelRect.Contains(Event.current.mousePosition))
            {
                var dragged = DragAndDrop.objectReferences.OfType<NestedType>().FirstOrDefault();
                if (dragged != null && dragged != node)
                {
                    // Check for circular dependency
                    bool wouldCauseCircular = false;
                    if (dragged != null)
                    {
                        var tempParents = dragged.Parents?.ToList() ?? new List<NestedType>();
                        if (!tempParents.Contains(node))
                            tempParents.Add(node);
                        dragged.Parents = tempParents.ToArray();
                        wouldCauseCircular = nestedTypes.HasCircularDependencies(dragged);
                        dragged.Parents = tempParents.Where(p => p != node).ToArray();
                    }

                    DragAndDrop.visualMode = wouldCauseCircular ? DragAndDropVisualMode.Rejected : DragAndDropVisualMode.Link;
                    GUI.color = wouldCauseCircular ? Color.red : Color.green;

                    if (Event.current.type == EventType.DragPerform && !wouldCauseCircular)
                    {
                        Undo.RecordObject(dragged, "Set NestedType Parent(s)");
                        var newParents = dragged.Parents?.ToList() ?? new List<NestedType>();
                        if (!newParents.Contains(node))
                        {
                            if (Event.current.control)
                            {
                                // Append to existing parents if Ctrl is held
                                newParents.Add(node);
                            }
                            else
                            {
                                // Replace with single parent if Ctrl is not held
                                newParents = new List<NestedType> { node };
                            }
                            dragged.Parents = newParents.ToArray();
                        }
                        EditorUtility.SetDirty(dragged);
                        nestedTypes.InvalidateCache();
                        DragAndDrop.AcceptDrag();
                        Event.current.Use();
                    }
                    GUI.color = Color.white;
                }
            }

            if (Event.current.type == EventType.MouseDown && labelRect.Contains(Event.current.mousePosition))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new UnityEngine.Object[] { node };
                DragAndDrop.StartDrag("Dragging NestedType");
                Event.current.Use();
            }

            EditorGUILayout.EndHorizontal();

            if (hasChildren && foldoutStates[node])
            {
                foreach (var child in nestedTypes.GetChildren(node))
                {
                    DrawNode(child, indent + 1);
                }
            }
        }
    }
}
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// A custom Unity Editor window for editing multiple ScriptableObject instances of the same type.
    /// </summary>
    public class MultiScriptableObjectEditor : EditorWindow
    {
        private readonly ScriptableObjectTableView _tableView = new ScriptableObjectTableView();
        private readonly List<ScriptableObject> _scriptableObjects = new List<ScriptableObject>();

        /// <summary>
        /// Opens the Multi ScriptableObject Editor window from the Unity Editor menu.
        /// </summary>
        [MenuItem("Tools/LegendaryTools/ScriptableObjects/Multi ScriptableObject Editor")]
        public static void OpenWindow()
        {
            GetWindow<MultiScriptableObjectEditor>("Multi ScriptableObject Editor");
        }

        /// <summary>
        /// Called when the window is enabled. Initializes the table view with the current list of ScriptableObjects.
        /// </summary>
        private void OnEnable()
        {
            _tableView.SetScriptableObjects(_scriptableObjects);
        }

        /// <summary>
        /// Renders the GUI for the editor window, including a drag-and-drop area and the ScriptableObject table.
        /// </summary>
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Multi ScriptableObject Editor", EditorStyles.boldLabel);

            Rect dropArea = GUILayoutUtility.GetRect(0f, 50f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag ScriptableObjects Here");
            HandleDragAndDrop(dropArea);

            _tableView.DrawTable();
        }

        /// <summary>
        /// Handles drag-and-drop events to add ScriptableObjects to the editor.
        /// </summary>
        /// <param name="dropArea">The rectangle defining the drag-and-drop area.</param>
        private void HandleDragAndDrop(Rect dropArea)
        {
            Event currentEvent = Event.current;

            if (currentEvent.type == EventType.DragUpdated && dropArea.Contains(currentEvent.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.DragPerform && dropArea.Contains(currentEvent.mousePosition))
            {
                DragAndDrop.AcceptDrag();

                foreach (Object draggedObject in DragAndDrop.objectReferences)
                {
                    if (draggedObject is ScriptableObject scriptableObject && !_scriptableObjects.Contains(scriptableObject))
                    {
                        // Only add if the list is empty or the dragged object is of the same type as existing objects.
                        if (_scriptableObjects.Count == 0 || 
                            (_scriptableObjects[0] != null && scriptableObject.GetType() == _scriptableObjects[0].GetType()))
                        {
                            _scriptableObjects.Add(scriptableObject);
                        }
                    }
                }

                _tableView.SetScriptableObjects(_scriptableObjects);
                currentEvent.Use();
            }
        }
    }
}

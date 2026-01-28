using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    /// <summary>
    /// Enables dropping sprites dragged from the UI Palette window into the SceneView and Hierarchy.
    /// </summary>
    [InitializeOnLoad]
    internal static class UIPaletteDragDropHandler
    {
        static UIPaletteDragDropHandler()
        {
            SceneView.duringSceneGui += OnSceneViewGUI;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
        }

        private static void OnSceneViewGUI(SceneView sceneView)
        {
            Event e = Event.current;
            if (e == null)
                return;

            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
                return;

            // Strict gate: only handle our palette drags.
            if (DragAndDrop.GetGenericData(UIPaletteUtilities.DragSpriteGuidKey) == null)
                return;

            if (!UIPaletteUtilities.TryGetDraggedSprite(out Sprite sprite, out _))
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                Vector2 screenPos = HandleUtility.GUIPointToScreenPixelCoordinate(e.mousePosition);
                Camera eventCamera = sceneView != null ? sceneView.camera : null;

                GameObject dropTarget = Selection.activeGameObject;
                bool useNativeSize = e.shift;

                UIPaletteUtilities.CreateUIImageForDrop(sprite, dropTarget, screenPos, eventCamera, useNativeSize);
            }

            e.Use();
        }

        private static void OnHierarchyItemGUI(int instanceId, Rect selectionRect)
        {
            Event e = Event.current;
            if (e == null)
                return;

            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)
                return;

            if (!selectionRect.Contains(e.mousePosition))
                return;

            // Strict gate: only handle our palette drags.
            if (DragAndDrop.GetGenericData(UIPaletteUtilities.DragSpriteGuidKey) == null)
                return;

            if (!UIPaletteUtilities.TryGetDraggedSprite(out Sprite sprite, out _))
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                GameObject dropTarget = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                bool useNativeSize = e.shift;

                // No reliable screen position in Hierarchy; default to anchoredPosition zero.
                UIPaletteUtilities.CreateUIImageForDrop(sprite, dropTarget, null, null, useNativeSize);
            }

            e.Use();
        }
    }
}
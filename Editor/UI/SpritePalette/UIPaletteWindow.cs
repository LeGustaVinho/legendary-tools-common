using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    public sealed class UIPaletteWindow : EditorWindow
    {
        private UIPaletteWindowController _controller;
        private Vector2 _scroll;

        [MenuItem("Tools/LegendaryTools/UI/UI Palette")]
        public static void Open()
        {
            UIPaletteWindow window = GetWindow<UIPaletteWindow>();
            window.titleContent = new GUIContent("UI Palette");
            window.minSize = new Vector2(360f, 240f);
            window.Show();
        }

        private void OnEnable()
        {
            _controller = new UIPaletteWindowController(Repaint);
            _controller.OnEnable();
        }

        private void OnDisable()
        {
            _controller?.OnDisable();
            _controller = null;
        }

        private void OnGUI()
        {
            if (_controller == null)
                return;

            _controller.BeginFrame(Event.current);

            UIPaletteToolbarResult toolbar = UIPaletteWindowView.DrawToolbar(_controller.State);
            _controller.ApplyToolbar(toolbar);

            GUILayout.Space(4f);

            Rect dropRect = default;

            if (_controller.IsPaletteTab)
            {
                bool isDraggingSprites = _controller.IsDraggingSpritesFromProject();
                dropRect = UIPaletteWindowView.DrawDropZone(isDraggingSprites);
                _controller.HandleDropZone(dropRect, Event.current);
                GUILayout.Space(6f);
            }
            else
            {
                _controller.HandleWindowDropFallback(new Rect(0f, 0f, position.width, position.height), Event.current);
            }

            List<string> list = _controller.GetFilteredGuids();
            UIPaletteWindowView.DrawStats(_controller.GetStatsText(list.Count));

            GUILayout.Space(4f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            Dictionary<string, Rect> cellRects = UIPaletteWindowView.DrawGrid(
                list,
                _controller,
                position.width);

            EditorGUILayout.EndScrollView();

            _controller.EndFrame(cellRects);
        }
    }
}
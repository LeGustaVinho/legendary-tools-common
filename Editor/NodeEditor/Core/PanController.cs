#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Handles right-mouse panning within the canvas (zoom removed — always 1:1).
    /// </summary>
    public class PanController
    {
        private bool _rmbPanning;

        /// <summary>
        /// Processes right-mouse drag panning, disabled over nodes/edges by the caller.
        /// </summary>
        public void HandlePan(NodeEditorContext ctx, Rect canvasRect, bool allowStart)
        {
            Event e = Event.current;
            if (!canvasRect.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDown && e.button == 1)
            {
                _rmbPanning = allowStart;
                if (_rmbPanning) e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 1 && _rmbPanning)
            {
                // 1:1 — no zoom factor
                ctx.Scroll -= e.delta;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 1)
            {
                _rmbPanning = false;
            }
        }
    }
}
#endif
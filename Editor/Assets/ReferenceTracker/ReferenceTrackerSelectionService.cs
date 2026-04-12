using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal sealed class ReferenceTrackerSelectionService
    {
        public bool TryGetSupportedSelection(out UnityEngine.Object target, out string status)
        {
            target = null;

            if (Selection.activeObject == null)
            {
                status = "Nothing is selected.";
                return false;
            }

            if (Selection.activeObject is GameObject || Selection.activeObject is Component)
            {
                target = Selection.activeObject;
                status = string.Format("Using selection: {0}", target.name);
                return true;
            }

            if (Selection.activeGameObject != null)
            {
                target = Selection.activeGameObject;
                status = string.Format("Using selection: {0}", target.name);
                return true;
            }

            status = "Selection is not a supported target.";
            return false;
        }
    }
}

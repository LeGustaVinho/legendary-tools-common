using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Systems.ScreenFlow.Editor
{
#if !ODIN_INSPECTOR
    [CustomEditor(typeof(ScreenFlowConfig))]
    [CanEditMultipleObjects]
    public class ScreenFlowConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button(nameof(ScreenFlowConfig.FindConfigs)))
            {
                (target as ScreenFlowConfig).FindConfigs();
            }
        }
    }
#endif
}
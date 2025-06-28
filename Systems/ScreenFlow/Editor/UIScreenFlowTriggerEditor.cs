using System;
using UnityEditor;

namespace LegendaryTools.Systems.ScreenFlow.Editor
{
    [CustomEditor(typeof(UIScreenFlowTrigger))]
    [CanEditMultipleObjects]
    public class UIScreenFlowTriggerEditor : UnityEditor.Editor
    {
        private SerializedProperty triggerModeProperty;
        private SerializedProperty uiEntityProperty;

        private void OnEnable()
        {
            triggerModeProperty = serializedObject.FindProperty(nameof(UIScreenFlowTrigger.Mode));
            uiEntityProperty = serializedObject.FindProperty(nameof(UIScreenFlowTrigger.UiEntity));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(triggerModeProperty);

            ScreenFlowTriggerMode currentMode = (ScreenFlowTriggerMode) Enum.ToObject(typeof(ScreenFlowTriggerMode),
                triggerModeProperty.enumValueIndex);

            if (currentMode == ScreenFlowTriggerMode.Trigger)
            {
                EditorGUILayout.PropertyField(uiEntityProperty);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
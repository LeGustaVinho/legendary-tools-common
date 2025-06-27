namespace LegendaryTools.Systems.ScreenFlow.Editor
{
    using UnityEditor;
    using UnityEngine;

    [CustomPropertyDrawer(typeof(AnimationSystemConfig))]
    public class AnimationSystemConfigDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // Get AnimationSystem property
            SerializedProperty animationSystemProp = property.FindPropertyRelative("AnimationSystem");
            float height = EditorGUI.GetPropertyHeight(animationSystemProp);

            // Draw AnimationSystem enum field
            Rect enumRect = new(position.x, position.y, position.width, height);
            EditorGUI.PropertyField(enumRect, animationSystemProp);

            // Adjust position for next fields
            position.y += height + EditorGUIUtility.standardVerticalSpacing;

            AnimationSystem selectedSystem = (AnimationSystem)animationSystemProp.enumValueIndex;

            // Show fields based on selected AnimationSystem
            switch (selectedSystem)
            {
                case AnimationSystem.Animation:
                    DrawAnimationFields(position, property);
                    break;

                case AnimationSystem.Animator:
                    DrawAnimatorFields(position, property);
                    break;
#if DOTWEEN_PRO || DOTWEEN
            case AnimationSystem.DOTweenPro:
                DrawDoTweenFields(position, property);
                break;
#endif
#if UNITY_TIMELINE
            case AnimationSystem.UnityTimeline:
                DrawTimelineFields(position, property);
                break;
#endif
            }

            EditorGUI.EndProperty();
        }

        private void DrawAnimationFields(Rect position, SerializedProperty property)
        {
            SerializedProperty showAnimationProp = property.FindPropertyRelative("ShowAnimation");
            SerializedProperty hideAnimationProp = property.FindPropertyRelative("HideAnimation");

            float height = EditorGUI.GetPropertyHeight(showAnimationProp);

            // Draw ShowAnimation field
            Rect showRect = new(position.x, position.y, position.width, height);
            EditorGUI.PropertyField(showRect, showAnimationProp);

            // Draw HideAnimation field
            Rect hideRect = new(position.x, position.y + height + EditorGUIUtility.standardVerticalSpacing,
                position.width, height);
            EditorGUI.PropertyField(hideRect, hideAnimationProp);
        }

        private void DrawAnimatorFields(Rect position, SerializedProperty property)
        {
            SerializedProperty animatorProp = property.FindPropertyRelative("Animator");
            SerializedProperty showTriggerProp = property.FindPropertyRelative("ShowTriggerName");
            SerializedProperty hideTriggerProp = property.FindPropertyRelative("HideTriggerName");

            float height = EditorGUI.GetPropertyHeight(animatorProp);

            // Draw Animator field
            Rect animatorRect = new(position.x, position.y, position.width, height);
            EditorGUI.PropertyField(animatorRect, animatorProp);

            // Draw ShowTriggerName field
            Rect showRect = new(position.x, position.y + height + EditorGUIUtility.standardVerticalSpacing,
                position.width, height);
            EditorGUI.PropertyField(showRect, showTriggerProp);

            // Draw HideTriggerName field
            Rect hideRect = new(position.x, position.y + (height + EditorGUIUtility.standardVerticalSpacing) * 2,
                position.width, height);
            EditorGUI.PropertyField(hideRect, hideTriggerProp);
        }

        private void DrawDoTweenFields(Rect position, SerializedProperty property)
        {
            SerializedProperty doTweenShowProp = property.FindPropertyRelative("DoTweenShowAnimation");
            SerializedProperty doTweenHideProp = property.FindPropertyRelative("DoTweenHideAnimation");

            float height = EditorGUI.GetPropertyHeight(doTweenShowProp);

            // Draw DoTweenShowAnimation field
            Rect showRect = new(position.x, position.y, position.width, height);
            EditorGUI.PropertyField(showRect, doTweenShowProp);

            // Draw DoTweenHideAnimation field
            Rect hideRect = new(position.x, position.y + height + EditorGUIUtility.standardVerticalSpacing,
                position.width, height);
            EditorGUI.PropertyField(hideRect, doTweenHideProp);
        }

        private void DrawTimelineFields(Rect position, SerializedProperty property)
        {
            SerializedProperty timelineDirectorProp = property.FindPropertyRelative("TimelineDirector");
            SerializedProperty showTimelineProp = property.FindPropertyRelative("ShowTimeline");
            SerializedProperty hideTimelineProp = property.FindPropertyRelative("HideTimeline");

            float height = EditorGUI.GetPropertyHeight(timelineDirectorProp);

            // Draw TimelineDirector field
            Rect directorRect = new(position.x, position.y, position.width, height);
            EditorGUI.PropertyField(directorRect, timelineDirectorProp);

            // Draw ShowTimeline field
            Rect showRect = new(position.x, position.y + height + EditorGUIUtility.standardVerticalSpacing,
                position.width, height);
            EditorGUI.PropertyField(showRect, showTimelineProp);

            // Draw HideTimeline field
            Rect hideRect = new(position.x, position.y + (height + EditorGUIUtility.standardVerticalSpacing) * 2,
                position.width, height);
            EditorGUI.PropertyField(hideRect, hideTimelineProp);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            SerializedProperty animationSystemProp = property.FindPropertyRelative("AnimationSystem");
            AnimationSystem selectedSystem = (AnimationSystem)animationSystemProp.enumValueIndex;

            float totalHeight = EditorGUI.GetPropertyHeight(animationSystemProp);

            switch (selectedSystem)
            {
                case AnimationSystem.Animation:
                    totalHeight += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("ShowAnimation")) * 2;
                    totalHeight += EditorGUIUtility.standardVerticalSpacing;
                    break;

                case AnimationSystem.Animator:
                    totalHeight += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("Animator")) * 3;
                    totalHeight += EditorGUIUtility.standardVerticalSpacing * 2;
                    break;
#if DOTWEEN_PRO || DOTWEEN
            case AnimationSystem.DOTweenPro:
                totalHeight += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("DoTweenShowAnimation")) * 2;
                totalHeight += EditorGUIUtility.standardVerticalSpacing;
                break;
#endif
#if UNITY_TIMELINE
            case AnimationSystem.UnityTimeline:
                totalHeight += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("TimelineDirector")) * 3;
                totalHeight += EditorGUIUtility.standardVerticalSpacing * 2;
                break;
#endif
            }

            return totalHeight;
        }
    }
}
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    [CustomPropertyDrawer(typeof(SerializedTimeSpan))]
    public class SerializedTimeSpanDrawer : PropertyDrawer
    {
        private const float LabelWidth = 15f;
        private const float FieldWidth = 40f;
        private const float Padding = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            Rect fieldsRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y,
                position.width - EditorGUIUtility.labelWidth, position.height);
            
            EditorGUI.LabelField(labelRect, label);
            EditorGUI.BeginChangeCheck();
            
            float x = fieldsRect.x;
            float y = fieldsRect.y;
            float height = fieldsRect.height;
            
            Rect sep0Rect = new Rect(x, y + height / 4, LabelWidth, height / 2);
            EditorGUI.LabelField(sep0Rect, "D");
            x += sep0Rect.width + Padding;
            
            Rect daysRect = new Rect(x, y, FieldWidth, height);
            SerializedProperty daysProp = property.FindPropertyRelative("day");
            daysProp.intValue = Mathf.Max(0, EditorGUI.IntField(daysRect, daysProp.intValue));
            x += FieldWidth + Padding;
            
            Rect sep1Rect = new Rect(x, y + height / 4, LabelWidth, height / 2);
            EditorGUI.LabelField(sep1Rect, "H");
            x += sep1Rect.width + Padding;
            
            Rect hoursRect = new Rect(x, y, FieldWidth, height);
            SerializedProperty hoursProp = property.FindPropertyRelative("hour");
            hoursProp.intValue = Mathf.Max(0, EditorGUI.IntField(hoursRect, hoursProp.intValue));
            x += FieldWidth + Padding;
            
            Rect sep2Rect = new Rect(x, y + height / 4, LabelWidth, height / 2);
            EditorGUI.LabelField(sep2Rect, "M");
            x += sep2Rect.width + Padding;
            
            Rect minutesRect = new Rect(x, y, FieldWidth, height);
            SerializedProperty minutesProp = property.FindPropertyRelative("minute");
            minutesProp.intValue = Mathf.Clamp(EditorGUI.IntField(minutesRect, minutesProp.intValue), 0, 59);
            x += FieldWidth + Padding;
            
            Rect sep3Rect = new Rect(x, y + height / 4, LabelWidth, height / 2);
            EditorGUI.LabelField(sep3Rect, "S");
            x += sep3Rect.width + Padding;
            
            Rect secondsRect = new Rect(x, y, FieldWidth, height);
            SerializedProperty secondsProp = property.FindPropertyRelative("second");
            secondsProp.intValue = Mathf.Clamp(EditorGUI.IntField(secondsRect, secondsProp.intValue), 0, 59);
            //x += FieldWidth + Padding;
            
            if (EditorGUI.EndChangeCheck()) property.serializedObject.ApplyModifiedProperties();
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
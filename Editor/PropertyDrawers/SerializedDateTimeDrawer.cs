#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    [CustomPropertyDrawer(typeof(SerializedDateTime))]
    public class SerializedDateTimeDrawer : PropertyDrawer
    {
        private const float fieldWidth = 40f;
        private const float labelWidth = 15f;
        private const float spacing = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, label);
            
            float x = position.x + EditorGUIUtility.labelWidth;
            float y = position.y;
            
            Rect yearLabelRect = new Rect(x, y, labelWidth, EditorGUIUtility.singleLineHeight);
            Rect yearFieldRect = new Rect(x + labelWidth, y, fieldWidth, EditorGUIUtility.singleLineHeight);
            SerializedProperty yearProp = property.FindPropertyRelative("year");
            EditorGUI.LabelField(yearLabelRect, "Y");
            EditorGUI.BeginChangeCheck();
            int newYear = EditorGUI.IntField(yearFieldRect, yearProp.intValue);
            if (EditorGUI.EndChangeCheck())
            {
                newYear = Mathf.Clamp(newYear, 1, 9999);
                yearProp.intValue = newYear;
            }

            x += labelWidth + fieldWidth + spacing;
            
            Rect monthLabelRect = new Rect(x, y, labelWidth, EditorGUIUtility.singleLineHeight);
            Rect monthFieldRect = new Rect(x + labelWidth, y, fieldWidth, EditorGUIUtility.singleLineHeight);
            SerializedProperty monthProp = property.FindPropertyRelative("month");
            EditorGUI.LabelField(monthLabelRect, "M");
            EditorGUI.BeginChangeCheck();
            int newMonth = EditorGUI.IntField(monthFieldRect, monthProp.intValue);
            if (EditorGUI.EndChangeCheck())
            {
                newMonth = Mathf.Clamp(newMonth, 1, 12);
                monthProp.intValue = newMonth;
            }

            x += labelWidth + fieldWidth + spacing;
            
            Rect dayLabelRect = new Rect(x, y, labelWidth, EditorGUIUtility.singleLineHeight);
            Rect dayFieldRect = new Rect(x + labelWidth, y, fieldWidth, EditorGUIUtility.singleLineHeight);
            SerializedProperty dayProp = property.FindPropertyRelative("day");
            EditorGUI.LabelField(dayLabelRect, "D");
            int maxDay = GetMaxDay(yearProp.intValue, monthProp.intValue);
            EditorGUI.BeginChangeCheck();
            int newDay = EditorGUI.IntField(dayFieldRect, dayProp.intValue);
            if (EditorGUI.EndChangeCheck())
            {
                newDay = Mathf.Clamp(newDay, 1, maxDay);
                dayProp.intValue = newDay;
            }

            x += labelWidth + fieldWidth + spacing;
            
            Rect hourLabelRect = new Rect(x, y, labelWidth, EditorGUIUtility.singleLineHeight);
            Rect hourFieldRect = new Rect(x + labelWidth, y, fieldWidth, EditorGUIUtility.singleLineHeight);
            SerializedProperty hourProp = property.FindPropertyRelative("hour");
            EditorGUI.LabelField(hourLabelRect, "H");
            EditorGUI.BeginChangeCheck();
            int newHour = EditorGUI.IntField(hourFieldRect, hourProp.intValue);
            if (EditorGUI.EndChangeCheck())
            {
                newHour = Mathf.Clamp(newHour, 0, 23);
                hourProp.intValue = newHour;
            }

            x += labelWidth + fieldWidth + spacing;
            
            Rect minuteLabelRect = new Rect(x, y, labelWidth, EditorGUIUtility.singleLineHeight);
            Rect minuteFieldRect = new Rect(x + labelWidth, y, fieldWidth, EditorGUIUtility.singleLineHeight);
            SerializedProperty minuteProp = property.FindPropertyRelative("minute");
            EditorGUI.LabelField(minuteLabelRect, "M");
            EditorGUI.BeginChangeCheck();
            int newMinute = EditorGUI.IntField(minuteFieldRect, minuteProp.intValue);
            if (EditorGUI.EndChangeCheck())
            {
                newMinute = Mathf.Clamp(newMinute, 0, 59);
                minuteProp.intValue = newMinute;
            }

            x += labelWidth + fieldWidth + spacing;
            
            Rect secondLabelRect = new Rect(x, y, labelWidth, EditorGUIUtility.singleLineHeight);
            Rect secondFieldRect = new Rect(x + labelWidth, y, fieldWidth, EditorGUIUtility.singleLineHeight);
            SerializedProperty secondProp = property.FindPropertyRelative("second");
            EditorGUI.LabelField(secondLabelRect, "S");
            EditorGUI.BeginChangeCheck();
            int newSecond = EditorGUI.IntField(secondFieldRect, secondProp.intValue);
            if (EditorGUI.EndChangeCheck())
            {
                newSecond = Mathf.Clamp(newSecond, 0, 59);
                secondProp.intValue = newSecond;
            }

            EditorGUI.EndProperty();
        }
        
        private int GetMaxDay(int year, int month)
        {
            if (month < 1 || month > 12 || year < 1 || year > 9999)
                return 31;

            return DateTime.DaysInMonth(year, month);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        }
    }
}
#endif
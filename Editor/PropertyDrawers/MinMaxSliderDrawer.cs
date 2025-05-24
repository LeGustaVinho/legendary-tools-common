using LegendaryTools.Inspector;
using UnityEditor;
using UnityEngine;
using System;
using System.Reflection;

namespace LegendaryTools.Editor
{
    [CustomPropertyDrawer(typeof(MinMaxSliderAttribute))]
    public class MinMaxSliderDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Validate property type
            if (property.propertyType != SerializedPropertyType.Vector2 &&
                property.propertyType != SerializedPropertyType.Vector2Int)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            // Get the attribute and target object
            MinMaxSliderAttribute attribute = this.attribute as MinMaxSliderAttribute;
            object target = property.serializedObject.targetObject;

            // Determine min and max bounds
            float minBound = attribute.MinValue;
            float maxBound = attribute.MaxValue;

            if (!string.IsNullOrEmpty(attribute.MinMaxValueGetter))
            {
                Vector2? minMaxValue = GetValueFromGetter(attribute.MinMaxValueGetter, target) as Vector2?;
                if (minMaxValue.HasValue)
                {
                    minBound = minMaxValue.Value.x;
                    maxBound = minMaxValue.Value.y;
                }
                else
                {
                    Debug.LogWarning(
                        $"MinMaxValueGetter '{attribute.MinMaxValueGetter}' did not return a Vector2. Using hardcoded values.");
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(attribute.MinValueGetter))
                {
                    object minValueObj = GetValueFromGetter(attribute.MinValueGetter, target);
                    if (minValueObj is float minFloat)
                        minBound = minFloat;
                    else
                        Debug.LogWarning(
                            $"MinValueGetter '{attribute.MinValueGetter}' did not return a float. Using hardcoded MinValue.");
                }

                if (!string.IsNullOrEmpty(attribute.MaxValueGetter))
                {
                    object maxValueObj = GetValueFromGetter(attribute.MaxValueGetter, target);
                    if (maxValueObj is float maxFloat)
                        maxBound = maxFloat;
                    else
                        Debug.LogWarning(
                            $"MaxValueGetter '{attribute.MaxValueGetter}' did not return a float. Using hardcoded MaxValue.");
                }
            }

            // Ensure minBound is not greater than maxBound
            if (minBound > maxBound)
            {
                float temp = minBound;
                minBound = maxBound;
                maxBound = temp;
            }

            // Get current property value
            Vector2 currentValue = property.propertyType == SerializedPropertyType.Vector2
                ? property.vector2Value
                : (Vector2)property.vector2IntValue;

            float minValue = currentValue.x;
            float maxValue = currentValue.y;

            // Draw the label and get the remaining control rect
            Rect controlRect = EditorGUI.PrefixLabel(position, label);

            // Draw UI elements
            if (attribute.ShowFields)
            {
                float fieldWidth = 50f;
                Rect minFieldRect = new(controlRect.x, controlRect.y, fieldWidth, controlRect.height);
                Rect sliderRect = new(controlRect.x + fieldWidth, controlRect.y, controlRect.width - 2 * fieldWidth,
                    controlRect.height);
                Rect maxFieldRect = new(controlRect.x + controlRect.width - fieldWidth, controlRect.y, fieldWidth,
                    controlRect.height);

                // Draw min field
                minValue = EditorGUI.FloatField(minFieldRect, minValue);

                // Draw slider
                EditorGUI.MinMaxSlider(sliderRect, ref minValue, ref maxValue, minBound, maxBound);

                // Draw max field
                maxValue = EditorGUI.FloatField(maxFieldRect, maxValue);
            }
            else
            {
                // Draw slider only
                EditorGUI.MinMaxSlider(controlRect, ref minValue, ref maxValue, minBound, maxBound);
            }

            // Clamp values to bounds and ensure max >= min
            minValue = Mathf.Clamp(minValue, minBound, maxBound);
            maxValue = Mathf.Clamp(maxValue, minBound, maxBound);
            if (maxValue < minValue) maxValue = minValue;

            // Update property value based on type
            if (property.propertyType == SerializedPropertyType.Vector2)
            {
                property.vector2Value = new Vector2(minValue, maxValue);
            }
            else if (property.propertyType == SerializedPropertyType.Vector2Int)
            {
                int minInt = Mathf.RoundToInt(minValue);
                int maxInt = Mathf.RoundToInt(maxValue);
                if (maxInt < minInt) maxInt = minInt;
                property.vector2IntValue = new Vector2Int(minInt, maxInt);
            }
        }

        private object GetValueFromGetter(string getter, object target)
        {
            if (string.IsNullOrEmpty(getter)) return null;

            Type type = target.GetType();
            FieldInfo field =
                type.GetField(getter, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field.GetValue(target);

            PropertyInfo property = type.GetProperty(getter,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null) return property.GetValue(target);

            return null;
        }
    }
}
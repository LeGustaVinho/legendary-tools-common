using System;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    public static class BindingSerializedValueDrawer
    {
        public static void Draw(
            SerializedProperty valueProperty,
            Type valueType,
            string label)
        {
            if (valueProperty == null)
            {
                return;
            }

            Type effectiveType = valueType == null
                ? null
                : Nullable.GetUnderlyingType(valueType) ?? valueType;

            if (effectiveType == null)
            {
                EditorGUILayout.PropertyField(
                    valueProperty.FindPropertyRelative("serializedValue"),
                    new GUIContent(label));
                EditorGUILayout.HelpBox(
                    "Select a Source member to get a typed value editor.",
                    MessageType.Info);
                return;
            }

            if (effectiveType == typeof(string))
            {
                EditorGUILayout.PropertyField(valueProperty.FindPropertyRelative("stringValue"), new GUIContent(label));
            }
            else if (effectiveType == typeof(bool))
            {
                EditorGUILayout.PropertyField(valueProperty.FindPropertyRelative("boolValue"), new GUIContent(label));
            }
            else if (effectiveType == typeof(int))
            {
                EditorGUILayout.PropertyField(valueProperty.FindPropertyRelative("intValue"), new GUIContent(label));
            }
            else if (effectiveType == typeof(long))
            {
                EditorGUILayout.PropertyField(valueProperty.FindPropertyRelative("longValue"), new GUIContent(label));
            }
            else if (effectiveType == typeof(float))
            {
                EditorGUILayout.PropertyField(valueProperty.FindPropertyRelative("floatValue"), new GUIContent(label));
            }
            else if (effectiveType == typeof(double))
            {
                EditorGUILayout.PropertyField(valueProperty.FindPropertyRelative("doubleValue"), new GUIContent(label));
            }
            else if (effectiveType == typeof(Vector2))
            {
                EditorGUILayout.PropertyField(valueProperty.FindPropertyRelative("vector2Value"), new GUIContent(label));
            }
            else if (effectiveType == typeof(Vector3))
            {
                EditorGUILayout.PropertyField(valueProperty.FindPropertyRelative("vector3Value"), new GUIContent(label));
            }
            else if (effectiveType == typeof(Vector4))
            {
                EditorGUILayout.PropertyField(valueProperty.FindPropertyRelative("vector4Value"), new GUIContent(label));
            }
            else if (effectiveType == typeof(Color))
            {
                EditorGUILayout.PropertyField(valueProperty.FindPropertyRelative("colorValue"), new GUIContent(label));
            }
            else if (effectiveType == typeof(Quaternion))
            {
                EditorGUILayout.PropertyField(
                    valueProperty.FindPropertyRelative("quaternionValue"),
                    new GUIContent(label),
                    true);
            }
            else if (typeof(UnityEngine.Object).IsAssignableFrom(effectiveType))
            {
                SerializedProperty objectProperty = valueProperty.FindPropertyRelative("objectValue");
                objectProperty.objectReferenceValue = EditorGUILayout.ObjectField(
                    label,
                    objectProperty.objectReferenceValue,
                    effectiveType,
                    true);
            }
            else
            {
                SerializedProperty serializedValueProperty = valueProperty.FindPropertyRelative("serializedValue");
                if (effectiveType.IsEnum)
                {
                    string[] names = Enum.GetNames(effectiveType);
                    int selectedIndex = Array.IndexOf(names, serializedValueProperty.stringValue);
                    if (selectedIndex < 0)
                    {
                        selectedIndex = 0;
                    }

                    int newIndex = EditorGUILayout.Popup(label, selectedIndex, names);
                    if (names.Length > 0)
                    {
                        serializedValueProperty.stringValue = names[newIndex];
                    }
                }
                else
                {
                    EditorGUILayout.PropertyField(
                        serializedValueProperty,
                        new GUIContent(label));
                }

                if (!effectiveType.IsEnum &&
                    effectiveType != typeof(byte) &&
                    effectiveType != typeof(sbyte) &&
                    effectiveType != typeof(short) &&
                    effectiveType != typeof(ushort) &&
                    effectiveType != typeof(uint) &&
                    effectiveType != typeof(ulong) &&
                    effectiveType != typeof(decimal) &&
                    effectiveType != typeof(char))
                {
                    EditorGUILayout.LabelField(
                        $"JSON value for {effectiveType.Name}.",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
        }
    }
}

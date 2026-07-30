using System;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    [CustomPropertyDrawer(typeof(BindingContextValue))]
    public sealed class BindingContextValueDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            BindingContextValueKind kind = (BindingContextValueKind)property
                .FindPropertyRelative("kind")
                .enumValueIndex;
            int detailLines = kind == BindingContextValueKind.Provider ? 3 : 2;
            return EditorGUIUtility.singleLineHeight * (detailLines + 1) + Spacing * detailLines;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect line = new Rect(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                SerializedProperty kindProperty = property.FindPropertyRelative("kind");
                line.y += EditorGUIUtility.singleLineHeight + Spacing;
                EditorGUI.PropertyField(line, kindProperty, new GUIContent("Kind"));

                BindingContextValueKind kind = (BindingContextValueKind)kindProperty.enumValueIndex;
                line.y += EditorGUIUtility.singleLineHeight + Spacing;
                switch (kind)
                {
                    case BindingContextValueKind.UnityObject:
                        EditorGUI.PropertyField(
                            line,
                            property.FindPropertyRelative("objectReference"),
                            new GUIContent("Object"));
                        break;

                    case BindingContextValueKind.Provider:
                        EditorGUI.PropertyField(
                            line,
                            property.FindPropertyRelative("providerReference"),
                            new GUIContent("Provider"));
                        line.y += EditorGUIUtility.singleLineHeight + Spacing;
                        DrawTypeSelector(
                            line,
                            property.FindPropertyRelative("declaredTypeName"),
                            "Declared Type",
                            "Select Type");
                        break;

                    case BindingContextValueKind.StaticType:
                        DrawTypeSelector(
                            line,
                            property.FindPropertyRelative("staticTypeName"),
                            "Static Type",
                            "Select Type");
                        break;
                }
            }

            EditorGUI.EndProperty();
        }

        private static void DrawTypeSelector(
            Rect position,
            SerializedProperty typeProperty,
            string label,
            string emptyLabel)
        {
            Type currentType = DefaultBindingInstanceResolver.FindType(typeProperty.stringValue);
            string buttonLabel = currentType == null ? emptyLabel : currentType.FullName;
            Rect buttonRect = EditorGUI.PrefixLabel(position, new GUIContent(label));

            if (!GUI.Button(buttonRect, buttonLabel, BindingInspectorStyles.PathButtonStyle))
            {
                return;
            }

            SerializedObject serializedObject = typeProperty.serializedObject;
            string propertyPath = typeProperty.propertyPath;
            PopupWindow.Show(
                buttonRect,
                new StaticTypePickerWindow(type =>
                {
                    serializedObject.Update();
                    SerializedProperty refreshedProperty = serializedObject.FindProperty(propertyPath);
                    if (refreshedProperty != null)
                    {
                        refreshedProperty.stringValue = type.AssemblyQualifiedName;
                        serializedObject.ApplyModifiedProperties();
                    }
                }));
        }
    }
}

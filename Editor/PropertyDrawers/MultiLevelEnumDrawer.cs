using LegendaryTools.Editor.Attributes;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    [CustomPropertyDrawer(typeof(MultiLevelEnumAttribute))]
    public class MultiLevelEnumDrawer : PropertyDrawer
    {
        private GUIContent[] displayedOptions;
        private bool init;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType == SerializedPropertyType.Enum)
            {
                if (!init)
                {
                    displayedOptions = EnumGetNames(property);
                    init = true;
                }

                property.enumValueIndex = EditorGUI.Popup(position, label, property.enumValueIndex, displayedOptions);
            }
            else
            {
                EditorGUI.LabelField(position, label.text + " not Supported type: " + property.propertyType);
            }
        }

        private GUIContent[] EnumGetNames(SerializedProperty property)
        {
            GUIContent[] result = new GUIContent[property.enumNames.Length];

            for (int i = 0; i < property.enumNames.Length; i++)
            {
                result[i] = new GUIContent(property.enumNames[i].Replace('_', '/'));
            }

            return result;
        }
    }
}
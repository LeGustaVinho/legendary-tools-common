#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2.Editor
{
    /// <summary>
    /// Custom drawer for AttributeEntry inside EntityDefinition.
    /// Shows definition, base value, category and visibility.
    /// </summary>
    [CustomPropertyDrawer(typeof(AttributeEntry))]
    public class AttributeEntryDrawer : PropertyDrawer
    {
        private const float LineHeight = 18f;
        private const float LineSpacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return (LineHeight + LineSpacing) * 3f + 4f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty defProp = property.FindPropertyRelative("definition");
            SerializedProperty baseValueProp = property.FindPropertyRelative("baseValue");

            Rect line = new(position.x, position.y, position.width, LineHeight);

            // First line: definition reference
            EditorGUI.PropertyField(line, defProp, new GUIContent(label.text + " Definition"));

            line.y += LineHeight + LineSpacing;

            AttributeDefinition def = defProp.objectReferenceValue as AttributeDefinition;
            if (def == null)
            {
                EditorGUI.HelpBox(line, "Assign an AttributeDefinition.", MessageType.Info);
                EditorGUI.EndProperty();
                return;
            }

            DrawBaseValueField(line, baseValueProp, def);

            line.y += LineHeight + LineSpacing;

            DrawMetaLine(line, def);

            EditorGUI.EndProperty();
        }

        private void DrawBaseValueField(Rect rect, SerializedProperty baseValueProp, AttributeDefinition def)
        {
            SerializedProperty rawProp = baseValueProp.FindPropertyRelative("_raw");
            if (rawProp == null)
            {
                EditorGUI.LabelField(rect, "Base Value: (raw field not found)");
                return;
            }

            switch (def.kind)
            {
                case AttributeKind.Integer:
                    DrawIntValue(rect, rawProp, def);
                    break;

                case AttributeKind.Float:
                    DrawFloatValue(rect, rawProp, def);
                    break;

                case AttributeKind.Flags:
                    DrawFlagsValue(rect, rawProp, def);
                    break;

                default:
                    EditorGUI.LabelField(rect, "Base Value: Unsupported kind");
                    break;
            }
        }

        private void DrawIntValue(Rect rect, SerializedProperty rawProp, AttributeDefinition def)
        {
            long current = unchecked((long)rawProp.ulongValue);
            long newValue = EditorGUI.LongField(rect, new GUIContent("Base Integer"), current);

            if (newValue != current) rawProp.ulongValue = unchecked((ulong)newValue);
        }

        private void DrawFloatValue(Rect rect, SerializedProperty rawProp, AttributeDefinition def)
        {
            long bits = unchecked((long)rawProp.ulongValue);
            double current = System.BitConverter.Int64BitsToDouble(bits);

            double newValue = EditorGUI.DoubleField(rect, new GUIContent("Base Float"), current);
            if (!Mathf.Approximately((float)newValue, (float)current))
            {
                long newBits = System.BitConverter.DoubleToInt64Bits(newValue);
                rawProp.ulongValue = unchecked((ulong)newBits);
            }
        }

        private void DrawFlagsValue(Rect rect, SerializedProperty rawProp, AttributeDefinition def)
        {
            ulong mask = rawProp.ulongValue;
            string label = $"Base Flags: 0x{mask:X16}";
            EditorGUI.LabelField(rect, label);

            // Could be extended to show individual named flags in a foldout.
        }

        private void DrawMetaLine(Rect rect, AttributeDefinition def)
        {
            string category = string.IsNullOrEmpty(def.categoryName) ? "<No Category>" : def.categoryName;
            string visibility = def.visibility.ToString();

            string text = $"Category: {category}   Visibility: {visibility}";

            EditorGUI.LabelField(rect, text, EditorStyles.miniLabel);
        }
    }
}
#endif
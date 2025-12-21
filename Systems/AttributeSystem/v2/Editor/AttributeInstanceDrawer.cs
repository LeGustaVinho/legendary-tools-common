#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2.Editor
{
    /// <summary>
    /// Custom drawer for AttributeInstance.
    /// Editable base/value and friendly flags view.
    /// Designed for debug views.
    /// </summary>
    [CustomPropertyDrawer(typeof(AttributeInstance))]
    public class AttributeInstanceDrawer : PropertyDrawer
    {
        private const float LineHeight = 18f;
        private const float LineSpacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = (LineHeight + LineSpacing) * 4f + 6f; // def + base/value + meta + raw

            SerializedProperty defProp = property.FindPropertyRelative("_definition");
            AttributeDefinition def = defProp != null ? defProp.objectReferenceValue as AttributeDefinition : null;

            if (def != null && def.kind == AttributeKind.Flags)
            {
                int count = def.flagNames != null ? Mathf.Min(def.flagNames.Length, 64) : 0;

                if (count <= 0)
                    // Uma linha para "No flag names defined"
                    height += LineHeight + LineSpacing;
                else
                    // Header "Flags" + uma linha por flag
                    height += (LineHeight + LineSpacing) * (count + 1);
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty defProp = property.FindPropertyRelative("_definition");
            SerializedProperty baseValueProp = property.FindPropertyRelative("_baseValue");
            SerializedProperty valueProp = property.FindPropertyRelative("_value");

            Rect line = new(position.x, position.y, position.width, LineHeight);

            // Definition (read-only reference)
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.PropertyField(line, defProp, new GUIContent(label.text + " Definition"));
            }

            line.y += LineHeight + LineSpacing;

            AttributeDefinition def = defProp.objectReferenceValue as AttributeDefinition;
            if (def == null)
            {
                EditorGUI.HelpBox(line, "AttributeDefinition is null.", MessageType.Warning);
                EditorGUI.EndProperty();
                return;
            }

            SerializedProperty baseRawProp = baseValueProp.FindPropertyRelative("_raw");
            SerializedProperty valueRawProp = valueProp.FindPropertyRelative("_raw");

            // Base / Value interpretados e editáveis
            DrawTypedValueLine(line, baseRawProp, valueRawProp, def);
            line.y += LineHeight + LineSpacing;

            // Meta: categoria, visibilidade, clamp
            DrawMetaLine(line, def);
            line.y += LineHeight + LineSpacing;

            // Raw bits
            DrawRawBitsLine(line, baseRawProp, valueRawProp);
            line.y += LineHeight + LineSpacing;

            // Flags amigáveis se for kind = Flags
            if (def.kind == AttributeKind.Flags) DrawFlagsArea(ref line, baseRawProp, valueRawProp, def);

            EditorGUI.EndProperty();
        }

        private void DrawTypedValueLine(
            Rect rect,
            SerializedProperty baseRaw,
            SerializedProperty valueRaw,
            AttributeDefinition def)
        {
            Rect left = new(rect.x, rect.y, rect.width * 0.5f - 2f, rect.height);
            Rect right = new(rect.x + rect.width * 0.5f + 2f, rect.y, rect.width * 0.5f - 2f, rect.height);

            switch (def.kind)
            {
                case AttributeKind.Integer:
                {
                    long baseInt = unchecked((long)baseRaw.ulongValue);
                    long curInt = unchecked((long)valueRaw.ulongValue);

                    long newBase = EditorGUI.LongField(left, new GUIContent("Base"), baseInt);
                    long newValue = EditorGUI.LongField(right, new GUIContent("Value"), curInt);

                    if (newBase != baseInt)
                        baseRaw.ulongValue = unchecked((ulong)newBase);

                    if (newValue != curInt)
                        valueRaw.ulongValue = unchecked((ulong)newValue);
                    break;
                }

                case AttributeKind.Float:
                {
                    long baseBits = unchecked((long)baseRaw.ulongValue);
                    long curBits = unchecked((long)valueRaw.ulongValue);

                    double baseF = System.BitConverter.Int64BitsToDouble(baseBits);
                    double curF = System.BitConverter.Int64BitsToDouble(curBits);

                    double newBase = EditorGUI.DoubleField(left, new GUIContent("Base"), baseF);
                    double newValue = EditorGUI.DoubleField(right, new GUIContent("Value"), curF);

                    if (!Mathf.Approximately((float)newBase, (float)baseF))
                    {
                        long newBits = System.BitConverter.DoubleToInt64Bits(newBase);
                        baseRaw.ulongValue = unchecked((ulong)newBits);
                    }

                    if (!Mathf.Approximately((float)newValue, (float)curF))
                    {
                        long newBits = System.BitConverter.DoubleToInt64Bits(newValue);
                        valueRaw.ulongValue = unchecked((ulong)newBits);
                    }

                    break;
                }

                case AttributeKind.Flags:
                {
                    // Para flags, linha principal mostra resumo em hex.
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUI.LabelField(left, "Base Flags", $"0x{baseRaw.ulongValue:X16}");
                        EditorGUI.LabelField(right, "Value Flags", $"0x{valueRaw.ulongValue:X16}");
                    }

                    break;
                }

                default:
                    EditorGUI.LabelField(rect, "Unsupported kind");
                    break;
            }
        }

        private void DrawMetaLine(Rect rect, AttributeDefinition def)
        {
            string category = string.IsNullOrEmpty(def.categoryName) ? "<No Category>" : def.categoryName;
            string visibility = def.visibility.ToString();
            string clamp = def.clampMode.ToString();

            string text = $"Category: {category}   Visibility: {visibility}   Clamp: {clamp}";
            EditorGUI.LabelField(rect, text, EditorStyles.miniLabel);
        }

        private void DrawRawBitsLine(Rect rect, SerializedProperty baseRaw, SerializedProperty valueRaw)
        {
            string text = $"Raw Base: 0x{baseRaw.ulongValue:X16}   Raw Value: 0x{valueRaw.ulongValue:X16}";
            EditorGUI.LabelField(rect, text, EditorStyles.miniLabel);
        }

        private void DrawFlagsArea(
            ref Rect line,
            SerializedProperty baseRaw,
            SerializedProperty valueRaw,
            AttributeDefinition def)
        {
            string[] names = def.flagNames;
            int count = names != null ? Mathf.Min(names.Length, 64) : 0;

            if (count <= 0)
            {
                EditorGUI.LabelField(line, "Flags", EditorStyles.boldLabel);
                line.y += LineHeight + LineSpacing;
                EditorGUI.LabelField(line, "No flag names defined.", EditorStyles.miniLabel);
                line.y += LineHeight + LineSpacing;
                return;
            }

            EditorGUI.LabelField(line, "Flags", EditorStyles.boldLabel);
            line.y += LineHeight + LineSpacing;

            ulong baseMask = baseRaw.ulongValue;
            ulong valueMask = valueRaw.ulongValue;

            for (int i = 0; i < count; i++)
            {
                string name = string.IsNullOrEmpty(names[i]) ? $"Flag {i}" : names[i];

                Rect labelRect = new(line.x, line.y, line.width * 0.4f, line.height);
                Rect baseRect = new(line.x + line.width * 0.4f + 2f, line.y, line.width * 0.25f - 2f, line.height);
                Rect valueRect = new(line.x + line.width * 0.65f + 4f, line.y, line.width * 0.35f - 4f, line.height);

                EditorGUI.LabelField(labelRect, $"[{i}] {name}");

                bool baseOn = (baseMask & (1UL << i)) != 0;
                bool newBaseOn = EditorGUI.ToggleLeft(baseRect, "Base", baseOn);
                if (newBaseOn != baseOn)
                {
                    if (newBaseOn)
                        baseMask |= 1UL << i;
                    else
                        baseMask &= ~(1UL << i);
                }

                bool valueOn = (valueMask & (1UL << i)) != 0;
                bool newValueOn = EditorGUI.ToggleLeft(valueRect, "Value", valueOn);
                if (newValueOn != valueOn)
                {
                    if (newValueOn)
                        valueMask |= 1UL << i;
                    else
                        valueMask &= ~(1UL << i);
                }

                line.y += LineHeight + LineSpacing;
            }

            baseRaw.ulongValue = baseMask;
            valueRaw.ulongValue = valueMask;
        }
    }
}
#endif
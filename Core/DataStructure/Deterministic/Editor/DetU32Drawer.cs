#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace DeterministicFixedPoint.Editor
{
    [CustomPropertyDrawer(typeof(DetU32))]
    public sealed class DetU32Drawer : PropertyDrawer
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private const int Scale = DetU32.Scale;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty rawProp = property.FindPropertyRelative("raw");
            if (rawProp == null || rawProp.propertyType != SerializedPropertyType.Integer)
            {
                EditorGUI.LabelField(position, label.text, "Invalid DetU32 backing field.");
                return;
            }

            Rect line1 = position;
            line1.height = EditorGUIUtility.singleLineHeight;

            Rect line2 = position;
            line2.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            line2.height = EditorGUIUtility.singleLineHeight;

            // Use uintValue for correct unsigned handling.
            uint raw = rawProp.uintValue;

            decimal value = raw / (decimal)Scale;
            string currentText = value.ToString("0.000", Invariant);

            EditorGUI.BeginProperty(line1, label, property);
            string newText = EditorGUI.DelayedTextField(line1, label, currentText);
            EditorGUI.EndProperty();

            if (!string.Equals(newText, currentText, StringComparison.Ordinal))
                if (TryParseDecimal(newText, out decimal parsed))
                {
                    if (parsed <= 0m)
                    {
                        rawProp.uintValue = 0u;
                    }
                    else
                    {
                        // Quantize deterministically: ties up.
                        decimal scaled = parsed * Scale;
                        ulong rounded = RoundDecimalNearestTiesUpToUInt64(scaled);

                        DetConfig.Lock();
                        uint newRaw;
                        if (DetConfig.Mode == DetOverflowMode.Wrap)
                            newRaw = unchecked((uint)rounded);
                        else
                            newRaw = ClampToUInt(rounded);

                        rawProp.uintValue = newRaw;
                    }
                }

            uint dbgRaw = rawProp.uintValue;
            string hex = dbgRaw.ToString("X8", Invariant);
            EditorGUI.LabelField(line2, "Raw / Hex", $"{dbgRaw} / 0x{hex}");
        }

        private static bool TryParseDecimal(string text, out decimal value)
        {
            string t = text?.Trim() ?? string.Empty;
            t = t.Replace(',', '.');
            return decimal.TryParse(t, NumberStyles.Float, Invariant, out value);
        }

        private static uint ClampToUInt(ulong v)
        {
            if (v > uint.MaxValue) return uint.MaxValue;
            return (uint)v;
        }

        private static ulong RoundDecimalNearestTiesUpToUInt64(decimal value)
        {
            if (value <= 0m) return 0UL;

            decimal trunc = decimal.Truncate(value);
            decimal frac = value - trunc;

            if (frac == 0m)
                return (ulong)trunc;

            if (frac > 0.5m)
                return (ulong)(trunc + 1m);

            if (frac == 0.5m)
                return (ulong)(trunc + 1m); // ties up

            return (ulong)trunc;
        }
    }
}
#endif
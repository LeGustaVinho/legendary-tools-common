#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace DeterministicFixedPoint.Editor
{
    [CustomPropertyDrawer(typeof(DetS32))]
    public sealed class DetS32Drawer : PropertyDrawer
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private const int Scale = DetS32.Scale;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Two lines: value + debug
            return EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty rawProp = property.FindPropertyRelative("raw");
            if (rawProp == null || rawProp.propertyType != SerializedPropertyType.Integer)
            {
                EditorGUI.LabelField(position, label.text, "Invalid DetS32 backing field.");
                return;
            }

            Rect line1 = position;
            line1.height = EditorGUIUtility.singleLineHeight;

            Rect line2 = position;
            line2.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            line2.height = EditorGUIUtility.singleLineHeight;

            int raw = rawProp.intValue;

            decimal value = raw / (decimal)Scale;
            string currentText = value.ToString("0.000", Invariant);

            EditorGUI.BeginProperty(line1, label, property);
            string newText = EditorGUI.DelayedTextField(line1, label, currentText);
            EditorGUI.EndProperty();

            if (!string.Equals(newText, currentText, StringComparison.Ordinal))
                if (TryParseDecimal(newText, out decimal parsed))
                {
                    decimal scaled = parsed * Scale;
                    long rounded = RoundDecimalAwayFromZeroToInt64(scaled);

                    DetConfig.Touch();
                    int newRaw;
                    if (DetConfig.Mode == DetOverflowMode.Wrap)
                        newRaw = unchecked((int)rounded);
                    else
                        newRaw = ClampToInt(rounded);

                    rawProp.intValue = newRaw;
                }

            int dbgRaw = rawProp.intValue;
            string hex = unchecked((uint)dbgRaw).ToString("X8", Invariant);
            EditorGUI.LabelField(line2, "Raw / Hex", $"{dbgRaw} / 0x{hex}");
        }

        private static bool TryParseDecimal(string text, out decimal value)
        {
            string t = text?.Trim() ?? string.Empty;
            t = t.Replace(',', '.');

            return decimal.TryParse(t, NumberStyles.Float, Invariant, out value);
        }

        private static int ClampToInt(long v)
        {
            if (v > int.MaxValue) return int.MaxValue;
            if (v < int.MinValue) return int.MinValue;
            return (int)v;
        }

        private static long RoundDecimalAwayFromZeroToInt64(decimal value)
        {
            decimal trunc = decimal.Truncate(value);
            decimal frac = value - trunc;

            if (frac == 0m)
                return (long)trunc;

            decimal absFrac = Math.Abs(frac); // FIXED (was decimal.Abs)

            if (absFrac > 0.5m)
                return (long)(trunc + Math.Sign(frac)); // FIXED (was decimal.Sign)

            if (absFrac == 0.5m)
                return (long)(trunc + Math.Sign(value)); // FIXED (was decimal.Sign) ties away from zero

            return (long)trunc;
        }
    }
}
#endif
#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Numerics;
using UnityEditor;
using UnityEngine;

namespace DeterministicFixedPoint.Editor
{
    [CustomPropertyDrawer(typeof(DetU64))]
    public sealed class DetU64Drawer : PropertyDrawer
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private const int Scale = DetU64.Scale;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty rawProp = property.FindPropertyRelative("raw");
            if (rawProp == null || rawProp.propertyType != SerializedPropertyType.Integer)
            {
                EditorGUI.LabelField(position, label.text, "Invalid DetU64 backing field.");
                return;
            }

            Rect line1 = position;
            line1.height = EditorGUIUtility.singleLineHeight;

            Rect line2 = position;
            line2.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            line2.height = EditorGUIUtility.singleLineHeight;

            ulong raw = rawProp.ulongValue;

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
                        rawProp.ulongValue = 0UL;
                    }
                    else
                    {
                        decimal scaled = parsed * Scale;
                        BigInteger rounded = RoundDecimalNearestTiesUpToBigInteger(scaled);

                        DetConfig.Touch();
                        ulong newRaw;
                        if (DetConfig.Mode == DetOverflowMode.Wrap)
                            newRaw = DetMath.WrapToUInt64(rounded);
                        else
                            newRaw = DetMath.ClampToUInt64(rounded);

                        rawProp.ulongValue = newRaw;
                    }
                }

            ulong dbgRaw = rawProp.ulongValue;
            string hex = dbgRaw.ToString("X16", Invariant);
            EditorGUI.LabelField(line2, "Raw / Hex", $"{dbgRaw} / 0x{hex}");
        }

        private static bool TryParseDecimal(string text, out decimal value)
        {
            string t = text?.Trim() ?? string.Empty;
            t = t.Replace(',', '.');
            return decimal.TryParse(t, NumberStyles.Float, Invariant, out value);
        }

        private static BigInteger RoundDecimalNearestTiesUpToBigInteger(decimal value)
        {
            if (value <= 0m) return BigInteger.Zero;

            decimal trunc = decimal.Truncate(value);
            decimal frac = value - trunc;

            BigInteger q = BigInteger.Parse(trunc.ToString("0", Invariant), Invariant);

            if (frac == 0m)
                return q;

            if (frac > 0.5m)
                return q + BigInteger.One;

            if (frac == 0.5m)
                return q + BigInteger.One; // ties up

            return q;
        }
    }
}
#endif
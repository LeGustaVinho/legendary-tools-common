#if UNITY_EDITOR
using System;
using System.Globalization;
using System.Numerics;
using UnityEditor;
using UnityEngine;

namespace DeterministicFixedPoint.Editor
{
    // One drawer for all fixed-point deterministic structs (DetS32, DetS64, DetU32, DetU64).
    [CustomPropertyDrawer(typeof(DetS32))]
    [CustomPropertyDrawer(typeof(DetS64))]
    [CustomPropertyDrawer(typeof(DetU32))]
    [CustomPropertyDrawer(typeof(DetU64))]
    public sealed class DetFixedPointDrawer : PropertyDrawer
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private const int FractionDigits = 3;

        private static readonly BigInteger MaskU32 = (BigInteger.One << 32) - BigInteger.One;

        private enum Kind
        {
            Unknown = 0,
            S32,
            S64,
            U32,
            U64
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Kind kind = GetKind();
            if (kind == Kind.Unknown)
            {
                EditorGUI.LabelField(position, label.text, "Unsupported type for DetFixedPointDrawer.");
                return;
            }

            SerializedProperty rawProp = property.FindPropertyRelative("raw");
            if (rawProp == null || rawProp.propertyType != SerializedPropertyType.Integer)
            {
                EditorGUI.LabelField(position, label.text, "Invalid backing field (expected 'raw').");
                return;
            }

            Rect line1 = position;
            line1.height = EditorGUIUtility.singleLineHeight;

            Rect line2 = position;
            line2.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            line2.height = EditorGUIUtility.singleLineHeight;

            // Display (do NOT touch DetConfig here, to avoid locking just by inspecting).
            decimal value = GetDecimalValueForDisplay(kind, rawProp);
            string currentText = FormatFixed(value);

            EditorGUI.BeginProperty(line1, label, property);
            string newText = EditorGUI.DelayedTextField(line1, label, currentText);
            EditorGUI.EndProperty();

            if (!string.Equals(newText, currentText, StringComparison.Ordinal))
            {
                if (TryParseDecimal(newText, out decimal parsed))
                {
                    BigInteger quantizedRaw = Quantize(kind, parsed);
                    ApplyRaw(kind, rawProp, quantizedRaw);
                }
            }

            EditorGUI.LabelField(line2, "Raw / Hex", GetDebugRawAndHex(kind, rawProp));
        }

        private Kind GetKind()
        {
            // fieldInfo.FieldType is the most reliable for PropertyDrawer.
            Type t = fieldInfo?.FieldType;
            if (t == typeof(DetS32)) return Kind.S32;
            if (t == typeof(DetS64)) return Kind.S64;
            if (t == typeof(DetU32)) return Kind.U32;
            if (t == typeof(DetU64)) return Kind.U64;
            return Kind.Unknown;
        }

        private static int GetScale(Kind kind)
        {
            // All your types currently use 1000, but keep this explicit per-type.
            switch (kind)
            {
                case Kind.S32: return DetS32.Scale;
                case Kind.S64: return DetS64.Scale;
                case Kind.U32: return DetU32.Scale;
                case Kind.U64: return DetU64.Scale;
                default: return 1000;
            }
        }

        private static decimal GetDecimalValueForDisplay(Kind kind, SerializedProperty rawProp)
        {
            int scale = GetScale(kind);

            switch (kind)
            {
                case Kind.S32:
                {
                    int raw = rawProp.intValue;
                    return raw / (decimal)scale;
                }
                case Kind.S64:
                {
                    long raw = rawProp.longValue;
                    return raw / (decimal)scale;
                }
                case Kind.U32:
                {
                    uint raw = rawProp.uintValue;
                    return raw / (decimal)scale;
                }
                case Kind.U64:
                {
                    ulong raw = rawProp.ulongValue;
                    return raw / (decimal)scale;
                }
                default:
                    return 0m;
            }
        }

        private static string GetDebugRawAndHex(Kind kind, SerializedProperty rawProp)
        {
            switch (kind)
            {
                case Kind.S32:
                {
                    int raw = rawProp.intValue;
                    string hex = unchecked((uint)raw).ToString("X8", Invariant);
                    return $"{raw} / 0x{hex}";
                }
                case Kind.S64:
                {
                    long raw = rawProp.longValue;
                    string hex = unchecked((ulong)raw).ToString("X16", Invariant);
                    return $"{raw} / 0x{hex}";
                }
                case Kind.U32:
                {
                    uint raw = rawProp.uintValue;
                    string hex = raw.ToString("X8", Invariant);
                    return $"{raw} / 0x{hex}";
                }
                case Kind.U64:
                {
                    ulong raw = rawProp.ulongValue;
                    string hex = raw.ToString("X16", Invariant);
                    return $"{raw} / 0x{hex}";
                }
                default:
                    return "0 / 0x0";
            }
        }

        private static string FormatFixed(decimal value)
        {
            return value.ToString("0.000", Invariant);
        }

        private static bool TryParseDecimal(string text, out decimal value)
        {
            string t = text?.Trim() ?? string.Empty;
            t = t.Replace(',', '.');
            return decimal.TryParse(t, NumberStyles.Float, Invariant, out value);
        }

        private static BigInteger Quantize(Kind kind, decimal authoredValue)
        {
            int scale = GetScale(kind);

            switch (kind)
            {
                // Signed: nearest, ties away from zero.
                case Kind.S32:
                case Kind.S64:
                {
                    decimal scaled = authoredValue * scale;
                    return RoundDecimalAwayFromZeroToBigInteger(scaled);
                }

                // Unsigned: clamp <= 0 to 0; nearest, ties up.
                case Kind.U32:
                case Kind.U64:
                {
                    if (authoredValue <= 0m)
                        return BigInteger.Zero;

                    decimal scaled = authoredValue * scale;
                    return RoundDecimalNearestTiesUpToBigInteger(scaled);
                }

                default:
                    return BigInteger.Zero;
            }
        }

        private static void ApplyRaw(Kind kind, SerializedProperty rawProp, BigInteger quantizedRaw)
        {
            // Only lock config when actually writing.
            DetConfig.Touch();

            switch (kind)
            {
                case Kind.S32:
                {
                    int newRaw;
                    if (DetConfig.Mode == DetOverflowMode.Wrap)
                        newRaw = WrapToInt32(quantizedRaw);
                    else
                        newRaw = ClampToInt32(quantizedRaw);

                    rawProp.intValue = newRaw;
                    break;
                }

                case Kind.S64:
                {
                    long newRaw;
                    if (DetConfig.Mode == DetOverflowMode.Wrap)
                        newRaw = DetMath.WrapToInt64(quantizedRaw);
                    else
                        newRaw = DetMath.ClampToInt64(quantizedRaw);

                    rawProp.longValue = newRaw;
                    break;
                }

                case Kind.U32:
                {
                    uint newRaw;
                    if (DetConfig.Mode == DetOverflowMode.Wrap)
                        newRaw = WrapToUInt32(quantizedRaw);
                    else
                        newRaw = ClampToUInt32(quantizedRaw);

                    rawProp.uintValue = newRaw;
                    break;
                }

                case Kind.U64:
                {
                    ulong newRaw;
                    if (DetConfig.Mode == DetOverflowMode.Wrap)
                        newRaw = DetMath.WrapToUInt64(quantizedRaw);
                    else
                        newRaw = DetMath.ClampToUInt64(quantizedRaw);

                    rawProp.ulongValue = newRaw;
                    break;
                }
            }
        }

        // -------------------------
        // Rounding helpers (decimal -> BigInteger)
        // -------------------------

        private static BigInteger RoundDecimalAwayFromZeroToBigInteger(decimal scaled)
        {
            decimal trunc = decimal.Truncate(scaled);
            decimal frac = scaled - trunc;

            BigInteger q = BigInteger.Parse(trunc.ToString("0", Invariant), Invariant);

            if (frac == 0m)
                return q;

            decimal absFrac = Math.Abs(frac);

            if (absFrac > 0.5m)
                return q + (Math.Sign(frac) > 0 ? BigInteger.One : BigInteger.Negate(BigInteger.One));

            if (absFrac == 0.5m)
                return q + (Math.Sign(scaled) > 0 ? BigInteger.One : BigInteger.Negate(BigInteger.One)); // ties away from zero

            return q;
        }

        private static BigInteger RoundDecimalNearestTiesUpToBigInteger(decimal scaled)
        {
            if (scaled <= 0m)
                return BigInteger.Zero;

            decimal trunc = decimal.Truncate(scaled);
            decimal frac = scaled - trunc;

            BigInteger q = BigInteger.Parse(trunc.ToString("0", Invariant), Invariant);

            if (frac == 0m)
                return q;

            if (frac > 0.5m)
                return q + BigInteger.One;

            if (frac == 0.5m)
                return q + BigInteger.One; // ties up

            return q;
        }

        // -------------------------
        // 32-bit wrap/clamp helpers via BigInteger
        // -------------------------

        private static int ClampToInt32(BigInteger v)
        {
            if (v > int.MaxValue) return int.MaxValue;
            if (v < int.MinValue) return int.MinValue;
            return (int)v;
        }

        private static uint ClampToUInt32(BigInteger v)
        {
            if (v.Sign <= 0) return 0u;
            if (v > uint.MaxValue) return uint.MaxValue;
            return (uint)v;
        }

        private static int WrapToInt32(BigInteger v)
        {
            BigInteger low = v & MaskU32;
            uint u = (uint)low;
            return unchecked((int)u);
        }

        private static uint WrapToUInt32(BigInteger v)
        {
            BigInteger low = v & MaskU32;
            return (uint)low;
        }
    }
}
#endif

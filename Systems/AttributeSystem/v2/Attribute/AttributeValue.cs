using System;
using UnityEngine;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Describes how an attribute value should be interpreted.
    /// </summary>
    public enum AttributeKind
    {
        Integer,
        Float,
        Flags
    }

    /// <summary>
    /// Attribute value stored in a single 8-byte field.
    /// Interpretation depends on AttributeKind in the definition.
    /// </summary>
    [Serializable]
    public struct AttributeValue
    {
        [SerializeField] private ulong _raw; // Single 8-byte storage

        public ulong Raw => _raw;

        /// <summary>
        /// Creates an AttributeValue from a signed integer.
        /// </summary>
        public static AttributeValue FromInt(long value)
        {
            return new AttributeValue { _raw = unchecked((ulong)value) };
        }

        /// <summary>
        /// Reads the value as a signed integer.
        /// </summary>
        public long ToInt()
        {
            return unchecked((long)_raw);
        }

        /// <summary>
        /// Creates an AttributeValue from a double-precision float.
        /// </summary>
        public static AttributeValue FromFloat(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            return new AttributeValue { _raw = unchecked((ulong)bits) };
        }

        /// <summary>
        /// Reads the value as a double-precision float.
        /// </summary>
        public double ToFloat()
        {
            long bits = unchecked((long)_raw);
            return BitConverter.Int64BitsToDouble(bits);
        }

        /// <summary>
        /// Creates an AttributeValue from a flags bitmask.
        /// </summary>
        public static AttributeValue FromFlags(ulong flags)
        {
            return new AttributeValue { _raw = flags };
        }

        /// <summary>
        /// Reads the value as a flags bitmask.
        /// </summary>
        public ulong ToFlags()
        {
            return _raw;
        }

        /// <summary>
        /// Checks whether a flag bit is set.
        /// </summary>
        public bool HasFlag(int bitIndex)
        {
            if (bitIndex < 0 || bitIndex > 63)
                throw new ArgumentOutOfRangeException(nameof(bitIndex));

            ulong mask = 1UL << bitIndex;
            return (_raw & mask) != 0;
        }

        /// <summary>
        /// Returns a new AttributeValue with a flag bit set.
        /// </summary>
        public AttributeValue WithFlag(int bitIndex)
        {
            if (bitIndex < 0 || bitIndex > 63)
                throw new ArgumentOutOfRangeException(nameof(bitIndex));

            ulong mask = 1UL << bitIndex;
            return new AttributeValue { _raw = _raw | mask };
        }

        /// <summary>
        /// Returns a new AttributeValue with a flag bit cleared.
        /// </summary>
        public AttributeValue WithoutFlag(int bitIndex)
        {
            if (bitIndex < 0 || bitIndex > 63)
                throw new ArgumentOutOfRangeException(nameof(bitIndex));

            ulong mask = ~(1UL << bitIndex);
            return new AttributeValue { _raw = _raw & mask };
        }

        public override string ToString()
        {
            return _raw.ToString();
        }
    }
}
using System;
using System.Globalization;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Implementation of INumberOperations for long (System.Int64).
    /// </summary>
    public sealed class Int64NumberOperations : INumberOperations<long>
    {
        public long Zero => 0L;
        public long One => 1L;

        public long Add(long a, long b)
        {
            return a + b;
        }

        public long Subtract(long a, long b)
        {
            return a - b;
        }

        public long Multiply(long a, long b)
        {
            return a * b;
        }

        /// <summary>
        /// Integer division, truncated toward zero (C# default).
        /// </summary>
        public long Divide(long a, long b)
        {
            return a / b;
        }

        public long Negate(long a)
        {
            return -a;
        }

        /// <summary>
        /// Power with long result. Uses Math.Pow and converts back to long.
        /// Be aware of overflow and truncation.
        /// </summary>
        public long Power(long a, long b)
        {
            double result = Math.Pow(a, b);
            return Convert.ToInt64(result);
        }

        public long ParseLiteral(string text)
        {
            return long.Parse(text, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts a double to long using Convert.ToInt64 (rounds to nearest).
        /// </summary>
        public long FromDouble(double value)
        {
            return Convert.ToInt64(value);
        }

        public double ToDouble(long value)
        {
            return value;
        }

        public long FromBoolean(bool value)
        {
            return value ? 1L : 0L;
        }

        public bool ToBoolean(long value)
        {
            return value != 0L;
        }
    }
}
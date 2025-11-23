using System;
using System.Globalization;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Implementation of INumberOperations for int (System.Int32).
    /// Integer division and rounding behavior are explicit in comments.
    /// </summary>
    public sealed class Int32NumberOperations : INumberOperations<int>
    {
        public int Zero => 0;
        public int One => 1;

        public int Add(int a, int b)
        {
            return a + b;
        }

        public int Subtract(int a, int b)
        {
            return a - b;
        }

        public int Multiply(int a, int b)
        {
            return a * b;
        }

        /// <summary>
        /// Integer division, truncated toward zero (C# default).
        /// </summary>
        public int Divide(int a, int b)
        {
            return a / b;
        }

        public int Negate(int a)
        {
            return -a;
        }

        /// <summary>
        /// Power with integer result. Uses Math.Pow and converts back to int.
        /// Be aware of overflow and truncation.
        /// </summary>
        public int Power(int a, int b)
        {
            double result = Math.Pow(a, b);
            return Convert.ToInt32(result);
        }

        public int ParseLiteral(string text)
        {
            return int.Parse(text, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts a double to int using Convert.ToInt32 (rounds to nearest).
        /// </summary>
        public int FromDouble(double value)
        {
            return Convert.ToInt32(value);
        }

        public double ToDouble(int value)
        {
            return value;
        }

        public int FromBoolean(bool value)
        {
            return value ? 1 : 0;
        }

        public bool ToBoolean(int value)
        {
            return value != 0;
        }
    }
}
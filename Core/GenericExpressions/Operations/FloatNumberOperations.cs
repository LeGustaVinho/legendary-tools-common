using System;
using System.Globalization;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Implementation of INumberOperations for float.
    /// Useful when you want to match Unity's float-based APIs.
    /// </summary>
    public sealed class FloatNumberOperations : INumberOperations<float>
    {
        public float Zero => 0f;
        public float One => 1f;

        public float Add(float a, float b)
        {
            return a + b;
        }

        public float Subtract(float a, float b)
        {
            return a - b;
        }

        public float Multiply(float a, float b)
        {
            return a * b;
        }

        public float Divide(float a, float b)
        {
            return a / b;
        }

        public float Negate(float a)
        {
            return -a;
        }

        public float Power(float a, float b)
        {
            return (float)Math.Pow(a, b);
        }

        public float ParseLiteral(string text)
        {
            return float.Parse(text, CultureInfo.InvariantCulture);
        }

        public float FromDouble(double value)
        {
            return (float)value;
        }

        public double ToDouble(float value)
        {
            return value;
        }

        public float FromBoolean(bool value)
        {
            return value ? 1f : 0f;
        }

        public bool ToBoolean(float value)
        {
            return Math.Abs(value) > float.Epsilon;
        }
    }
}
using System;
using System.Globalization;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Implementation of INumberOperations for double.
    /// Recommended default for math-heavy expressions.
    /// </summary>
    public sealed class DoubleNumberOperations : INumberOperations<double>
    {
        public double Zero => 0d;
        public double One => 1d;

        public double Add(double a, double b)
        {
            return a + b;
        }

        public double Subtract(double a, double b)
        {
            return a - b;
        }

        public double Multiply(double a, double b)
        {
            return a * b;
        }

        public double Divide(double a, double b)
        {
            return a / b;
        }

        public double Negate(double a)
        {
            return -a;
        }

        public double Power(double a, double b)
        {
            return Math.Pow(a, b);
        }

        public double ParseLiteral(string text)
        {
            return double.Parse(text, CultureInfo.InvariantCulture);
        }

        public double FromDouble(double value)
        {
            return value;
        }

        public double ToDouble(double value)
        {
            return value;
        }

        public double FromBoolean(bool value)
        {
            return value ? 1.0 : 0.0;
        }

        public bool ToBoolean(double value)
        {
            return Math.Abs(value) > double.Epsilon;
        }
    }
}
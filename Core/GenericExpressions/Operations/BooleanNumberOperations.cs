using System;
using System.Globalization;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Implementation of INumberOperations for bool.
    /// Intended for purely logical/conditional expressions.
    /// Arithmetic operators are not supported and will throw.
    /// </summary>
    public sealed class BooleanNumberOperations : INumberOperations<bool>
    {
        public bool Zero => false;
        public bool One => true;

        /// <summary>
        /// Arithmetic operations are not supported for BooleanNumberOperations.
        /// Use logical operators (and, or, not) in the language instead.
        /// </summary>
        private static Exception ArithmeticNotSupported()
        {
            return new NotSupportedException(
                "Arithmetic operators (+, -, *, /, ^) are not supported for BooleanNumberOperations.");
        }

        public bool Add(bool a, bool b)
        {
            throw ArithmeticNotSupported();
        }

        public bool Subtract(bool a, bool b)
        {
            throw ArithmeticNotSupported();
        }

        public bool Multiply(bool a, bool b)
        {
            throw ArithmeticNotSupported();
        }

        public bool Divide(bool a, bool b)
        {
            throw ArithmeticNotSupported();
        }

        public bool Negate(bool a)
        {
            // Could be interpreted as logical NOT, but that is already handled
            // by the LogicalNotNode in the AST, not by numeric negation.
            throw ArithmeticNotSupported();
        }

        public bool Power(bool a, bool b)
        {
            throw ArithmeticNotSupported();
        }

        /// <summary>
        /// Parses numeric-like literals into boolean:
        /// 0 => false, any other number => true.
        /// </summary>
        public bool ParseLiteral(string text)
        {
            double d = double.Parse(text, CultureInfo.InvariantCulture);
            return Math.Abs(d) > double.Epsilon;
        }

        public bool FromDouble(double value)
        {
            return Math.Abs(value) > double.Epsilon;
        }

        public double ToDouble(bool value)
        {
            return value ? 1.0 : 0.0;
        }

        public bool FromBoolean(bool value)
        {
            return value;
        }

        public bool ToBoolean(bool value)
        {
            return value;
        }
    }
}
using System;
using System.Globalization;

namespace LegendaryTools.MiniCSharp
{
    internal static class RuntimeOperators
    {
        public static object Add(object left, object right)
        {
            if (left is string || right is string)
            {
                return Convert.ToString(left, CultureInfo.InvariantCulture) + Convert.ToString(right, CultureInfo.InvariantCulture);
            }

            if (IsFloating(left) || IsFloating(right))
            {
                return ToDouble(left) + ToDouble(right);
            }

            return IntegralResult(left, right, ToLong(left) + ToLong(right));
        }

        public static object Subtract(object left, object right)
        {
            if (IsFloating(left) || IsFloating(right))
            {
                return ToDouble(left) - ToDouble(right);
            }

            return IntegralResult(left, right, ToLong(left) - ToLong(right));
        }

        public static object Multiply(object left, object right)
        {
            if (IsFloating(left) || IsFloating(right))
            {
                return ToDouble(left) * ToDouble(right);
            }

            return IntegralResult(left, right, ToLong(left) * ToLong(right));
        }

        public static object Divide(object left, object right)
        {
            if (IsFloating(left) || IsFloating(right))
            {
                double divisor = ToDouble(right);

                if (Math.Abs(divisor) < double.Epsilon)
                {
                    throw new ScriptException("Division by zero.");
                }

                return ToDouble(left) / divisor;
            }

            long rightValue = ToLong(right);

            if (rightValue == 0)
            {
                throw new ScriptException("Division by zero.");
            }

            return IntegralResult(left, right, ToLong(left) / rightValue);
        }

        public static object Modulo(object left, object right)
        {
            if (IsFloating(left) || IsFloating(right))
            {
                double divisor = ToDouble(right);

                if (Math.Abs(divisor) < double.Epsilon)
                {
                    throw new ScriptException("Division by zero.");
                }

                return ToDouble(left) % divisor;
            }

            long rightValue = ToLong(right);

            if (rightValue == 0)
            {
                throw new ScriptException("Division by zero.");
            }

            return IntegralResult(left, right, ToLong(left) % rightValue);
        }

        public static object Negate(object value)
        {
            if (value is float)
            {
                return -(float)value;
            }

            if (value is double)
            {
                return -(double)value;
            }

            if (value is decimal)
            {
                return -(decimal)value;
            }

            if (value is long)
            {
                return -(long)value;
            }

            if (value is int)
            {
                return -(int)value;
            }

            if (value is short)
            {
                return -(short)value;
            }

            if (value is byte)
            {
                return -(byte)value;
            }

            return -ToLong(value);
        }

        public static object IncrementOrDecrement(object value, bool increment)
        {
            if (!IsNumber(value))
            {
                throw new ScriptException("Increment and decrement only support numeric values.");
            }

            if (value is float)
            {
                return (float)value + (increment ? 1f : -1f);
            }

            if (value is double)
            {
                return (double)value + (increment ? 1d : -1d);
            }

            if (value is decimal)
            {
                return (decimal)value + (increment ? 1m : -1m);
            }

            if (value is long)
            {
                return (long)value + (increment ? 1L : -1L);
            }

            if (value is int)
            {
                return (int)value + (increment ? 1 : -1);
            }

            if (value is short)
            {
                return (short)((short)value + (increment ? 1 : -1));
            }

            if (value is byte)
            {
                return (byte)((byte)value + (increment ? 1 : -1));
            }

            return ToLong(value) + (increment ? 1L : -1L);
        }

        public static bool AreEqual(object left, object right)
        {
            if (left == null || right == null)
            {
                return left == right;
            }

            if (IsNumber(left) && IsNumber(right))
            {
                return Math.Abs(ToDouble(left) - ToDouble(right)) < 0.000001d;
            }

            return object.Equals(left, right);
        }

        public static int Compare(object left, object right)
        {
            if (IsNumber(left) && IsNumber(right))
            {
                return ToDouble(left).CompareTo(ToDouble(right));
            }

            if (left is IComparable comparable)
            {
                object convertedRight = RuntimeConversion.ConvertTo(right, left.GetType());
                return comparable.CompareTo(convertedRight);
            }

            throw new ScriptException($"Values of type '{left?.GetType().Name ?? "null"}' are not comparable.");
        }

        private static object IntegralResult(object left, object right, long value)
        {
            if (left is long || right is long)
            {
                return value;
            }

            return checked((int)value);
        }

        private static bool IsNumber(object value)
        {
            return value is byte ||
                   value is short ||
                   value is int ||
                   value is long ||
                   value is float ||
                   value is double ||
                   value is decimal;
        }

        private static bool IsFloating(object value)
        {
            return value is float || value is double || value is decimal;
        }

        private static double ToDouble(object value)
        {
            if (!IsNumber(value))
            {
                throw new ScriptException($"Expected numeric value, got '{value?.GetType().Name ?? "null"}'.");
            }

            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        private static long ToLong(object value)
        {
            if (!IsNumber(value))
            {
                throw new ScriptException($"Expected numeric value, got '{value?.GetType().Name ?? "null"}'.");
            }

            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
    }
}
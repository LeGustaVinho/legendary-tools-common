namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Abstraction for numeric and boolean operations over a generic type.
    /// Implement this for each numeric type you want to support (double, float, int, long, bool, etc.).
    /// </summary>
    /// <typeparam name="T">Numeric type.</typeparam>
    public interface INumberOperations<T>
    {
        T Zero { get; }
        T One { get; }

        T Add(T a, T b);
        T Subtract(T a, T b);
        T Multiply(T a, T b);
        T Divide(T a, T b);
        T Negate(T a);
        T Power(T a, T b);

        /// <summary>
        /// Parses a numeric literal from string.
        /// </summary>
        T ParseLiteral(string text);

        /// <summary>
        /// Converts a double to this numeric type (useful for built-in functions).
        /// </summary>
        T FromDouble(double value);

        /// <summary>
        /// Converts this numeric type to double (useful for built-in functions).
        /// </summary>
        double ToDouble(T value);

        /// <summary>
        /// Converts a boolean value to this numeric type.
        /// </summary>
        T FromBoolean(bool value);

        /// <summary>
        /// Converts this numeric type to a boolean value.
        /// Typically non-zero is true, zero is false.
        /// </summary>
        bool ToBoolean(T value);
    }
}
using TMPro;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Entry points for fluent binding DSL (TMP_InputField).
    /// </summary>
    public static class FluentBindingExtensions_Input
    {
        /// <summary>
        /// Starts a fluent binding for TMP_InputField from an Observable{T}.
        /// </summary>
        public static InputFieldBindingBuilder<T> Bind<T>(this Observable<T> observable, TMP_InputField field)
            where T : System.IEquatable<T>, System.IComparable<T>, System.IComparable, System.IConvertible
        {
            return new InputFieldBindingBuilder<T>(observable, field);
        }
    }
}
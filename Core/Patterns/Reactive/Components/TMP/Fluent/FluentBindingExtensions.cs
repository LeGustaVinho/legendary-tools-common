using TMPro;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Entry points for fluent binding DSL.
    /// </summary>
    public static class FluentBindingExtensions
    {
        /// <summary>
        /// Starts a fluent binding for TMP_Text from an Observable{T}.
        /// </summary>
        public static TextBindingBuilder<T> Bind<T>(this Observable<T> observable, TMP_Text label)
            where T : System.IEquatable<T>, System.IComparable<T>, System.IComparable, System.IConvertible
        {
            return new TextBindingBuilder<T>(observable, label);
        }
    }
}
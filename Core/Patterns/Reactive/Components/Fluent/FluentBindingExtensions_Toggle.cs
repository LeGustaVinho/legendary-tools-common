using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Entry points for Toggle fluent binding DSL.
    /// </summary>
    public static class FluentBindingExtensions_Toggle
    {
        /// <summary>
        /// Starts a fluent binding for Toggle from an Observable{T}.
        /// </summary>
        public static ToggleBindingBuilder<T> Bind<T>(this Observable<T> observable, Toggle toggle)
            where T : System.IEquatable<T>, System.IComparable<T>, System.IComparable, System.IConvertible
        {
            return new ToggleBindingBuilder<T>(observable, toggle);
        }
    }
}
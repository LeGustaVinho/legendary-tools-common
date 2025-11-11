using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Entry points for Scrollbar fluent binding DSL.
    /// </summary>
    public static class FluentBindingExtensions_Scrollbar
    {
        /// <summary>
        /// Starts a fluent binding for Scrollbar from an Observable{T}.
        /// </summary>
        public static ScrollbarBindingBuilder<T> Bind<T>(this Observable<T> observable, Scrollbar scrollbar)
            where T : System.IEquatable<T>, System.IComparable<T>, System.IComparable, System.IConvertible
        {
            return new ScrollbarBindingBuilder<T>(observable, scrollbar);
        }
    }
}
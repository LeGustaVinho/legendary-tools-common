using UnityEngine;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Entry points for CanvasGroup fluent binding DSL.
    /// </summary>
    public static class FluentBindingExtensions_CanvasGroup
    {
        /// <summary>
        /// Starts a fluent binding for CanvasGroup from an Observable{TValue}.
        /// </summary>
        public static CanvasGroupBindingBuilder<TValue> Bind<TValue>(this Observable<TValue> observable,
            CanvasGroup group)
            where TValue : System.IEquatable<TValue>, System.IComparable<TValue>, System.IComparable,
            System.IConvertible
        {
            return new CanvasGroupBindingBuilder<TValue>(observable, group);
        }
    }
}
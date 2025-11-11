using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Entry points for RawImage fluent binding DSL.
    /// </summary>
    public static class FluentBindingExtensions_RawImage
    {
        /// <summary>
        /// Starts a fluent binding for RawImage from an Observable{TValue}.
        /// </summary>
        public static RawImageBindingBuilder<TValue> Bind<TValue>(this Observable<TValue> observable, RawImage rawImage)
            where TValue : System.IEquatable<TValue>, System.IComparable<TValue>, System.IComparable,
            System.IConvertible
        {
            return new RawImageBindingBuilder<TValue>(observable, rawImage);
        }
    }
}
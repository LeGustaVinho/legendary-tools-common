using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Entry points for Image fluent binding DSL.
    /// </summary>
    public static class FluentBindingExtensions_Image
    {
        /// <summary>
        /// Starts a fluent binding for Image from an Observable{TValue} that will drive FillAmount, etc.
        /// Note: For Color/Sprite we also provide overloads that accept corresponding Observable types in the builder.
        /// </summary>
        public static ImageBindingBuilder<TValue> Bind<TValue>(this Observable<TValue> observable, Image image)
            where TValue : System.IEquatable<TValue>, System.IComparable<TValue>, System.IComparable,
            System.IConvertible
        {
            return new ImageBindingBuilder<TValue>(observable, image);
        }
    }
}
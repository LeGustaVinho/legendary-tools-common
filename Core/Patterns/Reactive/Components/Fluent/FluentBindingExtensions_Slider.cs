using UnityEngine.UI;

namespace LegendaryTools.Reactive.UGUI
{
    /// <summary>
    /// Entry points for Slider fluent binding DSL.
    /// </summary>
    public static class FluentBindingExtensions_Slider
    {
        /// <summary>
        /// Starts a fluent binding for Slider from an Observable{T}.
        /// </summary>
        public static SliderBindingBuilder<T> Bind<T>(this Observable<T> observable, Slider slider)
            where T : System.IEquatable<T>, System.IComparable<T>, System.IComparable, System.IConvertible
        {
            return new SliderBindingBuilder<T>(observable, slider);
        }
    }
}
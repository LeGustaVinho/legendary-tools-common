using TMPro;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Entry points for TMP_Text fluent binding DSL.
    /// </summary>
    public static class FluentBindingExtensions_TMP_Text
    {
        public static TmpTextBindingBuilder<TValue> Bind<TValue>(this Observable<TValue> observable, TMP_Text label)
            where TValue : System.IEquatable<TValue>, System.IComparable<TValue>, System.IComparable,
            System.IConvertible
        {
            return new TmpTextBindingBuilder<TValue>(observable, label);
        }
    }
}
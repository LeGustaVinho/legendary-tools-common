using TMPro;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Entry points for TMP_Dropdown fluent binding DSL.
    /// </summary>
    public static class FluentBindingExtensions_TMP_Dropdown
    {
        /// <summary>
        /// Starts a fluent binding chain for TMP_Dropdown. TValue is only a carrier for the chain.
        /// </summary>
        public static TmpDropdownBindingBuilder<TValue> Bind<TValue>(this Observable<TValue> observable,
            TMP_Dropdown dropdown)
            where TValue : System.IEquatable<TValue>, System.IComparable<TValue>, System.IComparable,
            System.IConvertible
        {
            return new TmpDropdownBindingBuilder<TValue>(observable, dropdown);
        }
    }
}
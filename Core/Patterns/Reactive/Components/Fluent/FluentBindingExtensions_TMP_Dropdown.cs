using TMPro;

namespace LegendaryTools.Reactive.TMPro
{
    /// <summary>
    /// Entry point for TMP_Dropdown fluent binding DSL.
    /// Starts from ObservableList{TItem} because options are list-driven.
    /// </summary>
    public static class FluentBindingExtensions_TMP_Dropdown
    {
        /// <summary>
        /// Starts a fluent binding for TMP_Dropdown using an ObservableList{TItem} to drive options.
        /// </summary>
        public static DropdownBindingBuilder<TItem> Bind<TItem>(this ObservableList<TItem> list, TMP_Dropdown dropdown)
        {
            return new DropdownBindingBuilder<TItem>(list, dropdown);
        }
    }
}
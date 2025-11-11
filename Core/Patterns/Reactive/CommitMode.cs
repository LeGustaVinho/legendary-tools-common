using System;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Controls when UI changes are committed back to the Observable.
    /// </summary>
    public enum CommitMode
    {
        /// <summary>
        /// Commit on every change (TMP_InputField.onValueChanged).
        /// </summary>
        OnChange = 0,

        /// <summary>
        /// Commit when the input field ends editing (TMP_InputField.onEndEdit).
        /// </summary>
        OnEndEdit = 1,

        /// <summary>
        /// Commit when the input field is submitted (TMP_InputField.onSubmit).
        /// </summary>
        OnSubmit = 2
    }
}
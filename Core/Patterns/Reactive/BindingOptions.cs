using System;
using System.Globalization;
using UnityEngine;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Options used to control formatting, placeholders, and edit-mode behavior for a binding.
    /// </summary>
    [Serializable]
    public sealed class BindingOptions
    {
        /// <summary>
        /// Optional formatter provider. If null, CultureInfo.InvariantCulture is used.
        /// </summary>
        public IFormatProvider FormatProvider { get; set; } = CultureInfo.InvariantCulture;

        /// <summary>
        /// Placeholder used when the formatted value is null or invalid.
        /// </summary>
        public string NullOrInvalidPlaceholder { get; set; } = "<empty>";

        /// <summary>
        /// If true, allows UI updates in Edit Mode (outside Play). Requires an attached BindingAnchor.
        /// </summary>
        public bool UpdateInEditMode { get; set; } = false;

        /// <summary>
        /// If true, the binding unsubscribes on OnDisable and re-subscribes on OnEnable.
        /// </summary>
        public bool UnbindOnDisable { get; set; } = true;

        /// <summary>
        /// If true and the GameObject gets re-enabled, the binding resynchronizes the UI.
        /// </summary>
        public bool ResyncOnEnable { get; set; } = true;
    }
}
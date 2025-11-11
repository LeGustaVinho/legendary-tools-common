using System;
using UnityEngine;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Describes a binding instance for diagnostics and tooling.
    /// </summary>
    [Serializable]
    public sealed class BindingInfo
    {
        /// <summary>
        /// Runtime unique identifier of the binding (for diagnostics).
        /// </summary>
        public readonly Guid Id = Guid.NewGuid();

        /// <summary>
        /// Human readable binding kind (e.g., "TMP_Text.Text", "TMP_InputField.Text").
        /// </summary>
        public string Kind;

        /// <summary>
        /// Optional binding direction (e.g., TwoWay/ToUI/FromUI).
        /// </summary>
        public string Direction;

        /// <summary>
        /// Optional short description (free form).
        /// </summary>
        public string Description;

        /// <summary>
        /// The Unity object considered the target of this binding (usually a Component).
        /// </summary>
        public UnityEngine.Object Target;

        /// <summary>
        /// The MonoBehaviour that owns the lifetime of this binding, if any.
        /// </summary>
        public MonoBehaviour Owner;

        /// <summary>
        /// The anchor used for lifecycle and edit-mode policy, if any.
        /// </summary>
        public BindingAnchor Anchor;

        /// <summary>
        /// Options snapshot (format provider, placeholder, unbind policy, etc.).
        /// </summary>
        public BindingOptions Options;

        /// <summary>
        /// Optional callback that returns a short, current state snapshot (value, etc.).
        /// </summary>
        public Func<string> GetState;

        /// <summary>
        /// Optional extra tags to help filtering.
        /// </summary>
        public string[] Tags;

        /// <summary>
        /// UTC time the binding was created.
        /// </summary>
        public readonly DateTime CreatedUtc = DateTime.UtcNow;
    }
}
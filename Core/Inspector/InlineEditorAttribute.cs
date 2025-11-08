using UnityEngine;
using System;

namespace LegendaryTools.Inspector
{
    /// <summary>
    /// Draws an inline inspector for a referenced ScriptableObject/MonoBehaviour.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class InlineEditorAttribute : PropertyAttribute
    {
        /// <summary>
        /// Optional: starts expanded by default.
        /// </summary>
        public bool ExpandedByDefault { get; }

        public InlineEditorAttribute(bool expandedByDefault = true)
        {
            ExpandedByDefault = expandedByDefault;
        }
    }
}
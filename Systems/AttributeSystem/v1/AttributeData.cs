using System;
using UnityEngine;

namespace LegendaryTools.AttributeSystem
{
    [Serializable]
    public class AttributeData
    {
        public bool OptionsAreFlags;
        public string[] Options;

        public bool HasCapacity;
        public bool AllowExceedCapacity;
        public float MinCapacity;

        public bool HasMinMax;
        public Vector2 MinMaxValue;

        public float[] StackPenaults;

        /// <summary>
        /// Returns true if the data has defined Options. Read-only.
        /// </summary>
        public bool HasOptions => Options != null && Options.Length > 0;

        /// <summary>
        /// For flags: If there are N options, this calculates the bitmask that has all bits set to 1.
        /// Example: For 3 options, we want binary 111 => decimal 7. Read-only.
        /// </summary>
        public int FlagOptionEverythingValue => HasOptions
            ? (int)Mathf.Pow(2, Options.Length) - 1
            : 0;

        /// <summary>
        /// Returns true if the data has a non-empty StackPenaults array. Read-only.
        /// </summary>
        public bool HasStackPenault => StackPenaults != null && StackPenaults.Length > 0;
    }
}
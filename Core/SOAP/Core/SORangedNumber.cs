using System;
using UnityEngine;

namespace LegendaryTools.SOAP.Variables
{
    /// <summary>
    /// Base class for ranged numeric variables. Supports clamping to [Min, Max].
    /// </summary>
    public abstract class SORangedNumber<T> : SOVariable<T> where T : struct, IComparable<T>
    {
        [SerializeField] private bool _useClamp = false;
        [SerializeField] private T _min = default;
        [SerializeField] private T _max = default;

        /// <summary>Enable/disable clamping.</summary>
        public bool UseClamp
        {
            get => _useClamp;
            set => _useClamp = value;
        }

        /// <summary>Lower bound (inclusive).</summary>
        public T Min
        {
            get => _min;
            set => _min = value;
        }

        /// <summary>Upper bound (inclusive).</summary>
        public T Max
        {
            get => _max;
            set => _max = value;
        }

        /// <summary>
        /// Clamp helper. Override to implement numeric clamp logic for unsupported T.
        /// </summary>
        protected abstract T Clamp(T value, T min, T max);

        public override void SetValue(T value)
        {
            if (UseClamp)
                value = Clamp(value, Min, Max);
            base.SetValue(value);
        }

        public override void SetValueSilent(T value)
        {
            if (UseClamp)
                value = Clamp(value, Min, Max);
            base.SetValueSilent(value);
        }

        // Allow derived classes to override base SetValue methods
        protected virtual void SetValueBase(T value)
        {
            base.SetValue(value);
        }

        protected virtual void SetValueSilentBase(T value)
        {
            base.SetValueSilent(value);
        }
    }
}
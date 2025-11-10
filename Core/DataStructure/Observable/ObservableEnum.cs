using System;
using System.Collections.Generic;

namespace LegendaryTools
{
    /// <summary>
    /// Provides an observable wrapper around an enum value of type <typeparamref name="TEnum"/>.
    /// Notifies subscribers when the value changes to a different value.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    [Serializable]
    public class ObservableEnum<TEnum> :
        IObservable<TEnum>,
        IEquatable<ObservableEnum<TEnum>>,
        IComparable<ObservableEnum<TEnum>>,
        IComparable
        where TEnum : struct, Enum
    {
#if ODIN_INSPECTOR
        [UnityEngine.HideInInspector]
#endif
        [UnityEngine.SerializeField] protected TEnum value;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        /// <summary>
        /// Gets or sets the current enum value. Setting a different value raises <see cref="OnChanged"/>.
        /// </summary>
        public TEnum Value
        {
            get => value;
            set
            {
                if (EqualityComparer<TEnum>.Default.Equals(this.value, value))
                    return;

                TEnum oldValue = this.value;
                this.value = value;
                OnChanged?.Invoke(this, oldValue, value);
            }
        }

        /// <summary>
        /// Occurs after <see cref="Value"/> is changed to a different value. Parameters are (sender, oldValue, newValue).
        /// </summary>
        public event Action<IObservable<TEnum>, TEnum, TEnum> OnChanged;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObservableEnum{TEnum}"/> class with the default value.
        /// </summary>
        public ObservableEnum()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ObservableEnum{TEnum}"/> class with the specified value.
        /// </summary>
        /// <param name="value">The initial enum value.</param>
        public ObservableEnum(TEnum value)
        {
            this.value = value;
        }

        /// <summary>
        /// Sets the value without raising <see cref="OnChanged"/>.
        /// </summary>
        /// <param name="valueToSet">The enum value to set.</param>
        public void SilentSet(TEnum valueToSet)
        {
            value = valueToSet;
        }

        /// <summary>
        /// Implicitly constructs an <see cref="ObservableEnum{TEnum}"/> from an enum value.
        /// </summary>
        public static implicit operator ObservableEnum<TEnum>(TEnum v)
        {
            return new ObservableEnum<TEnum>(v);
        }

        /// <summary>
        /// Explicitly converts an <see cref="ObservableEnum{TEnum}"/> to its underlying enum value.
        /// </summary>
        public static explicit operator TEnum(ObservableEnum<TEnum> o)
        {
            return o.value;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return value.ToString();
        }

        // IComparable (object)
        /// <summary>
        /// Compares this instance with another object for ordering.
        /// </summary>
        /// <param name="obj">The other object to compare.</param>
        /// <returns>An integer indicating relative order.</returns>
        /// <exception cref="ArgumentException">Thrown when types are incompatible.</exception>
        public int CompareTo(object obj)
        {
            if (obj is null) return 1;

            if (obj is ObservableEnum<TEnum> otherObs)
                return CompareTo(otherObs);

            if (obj is TEnum otherEnum)
                return Comparer<TEnum>.Default.Compare(value, otherEnum);

            throw new ArgumentException($"Object must be of type {typeof(ObservableEnum<TEnum>)} or {typeof(TEnum)}");
        }

        // IComparable<ObservableEnum<TEnum>>
        /// <summary>
        /// Compares this instance with another observable enum of the same type.
        /// </summary>
        public int CompareTo(ObservableEnum<TEnum> other)
        {
            if (other is null) return 1;
            return Comparer<TEnum>.Default.Compare(value, other.value);
        }

        // IEquatable<ObservableEnum<TEnum>>
        /// <summary>
        /// Indicates whether this instance and another observable enum are equal.
        /// </summary>
        public bool Equals(ObservableEnum<TEnum> other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null) return false;
            return EqualityComparer<TEnum>.Default.Equals(value, other.value);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is ObservableEnum<TEnum> otherObs) return Equals(otherObs);
            if (obj is TEnum otherEnum) return EqualityComparer<TEnum>.Default.Equals(value, otherEnum);
            return false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        /// <summary>
        /// Determines whether two observable enums are equal.
        /// </summary>
        public static bool operator ==(ObservableEnum<TEnum> a, ObservableEnum<TEnum> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.Equals(b);
        }

        /// <summary>
        /// Determines whether two observable enums are not equal.
        /// </summary>
        public static bool operator !=(ObservableEnum<TEnum> a, ObservableEnum<TEnum> b)
        {
            return !(a == b);
        }
    }
}

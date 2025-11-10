using System;
using System.Collections.Generic;

namespace LegendaryTools
{
    /// <summary>
    /// Provides an observable wrapper around a value of type <typeparamref name="T"/>.
    /// Notifies subscribers when the value changes to a different value.
    /// </summary>
    /// <typeparam name="T">
    /// The underlying value type. Works with classes, structs, and enums.
    /// Equality and comparison rely on EqualityComparer<T>.Default and Comparer<T>.Default.
    /// </typeparam>
    [Serializable]
    public class Observable<T> :
        IObservable<T>,
        IEquatable<Observable<T>>,
        IComparable<Observable<T>>,
        IComparable,
        IConvertible
    {
#if ODIN_INSPECTOR
        [UnityEngine.HideInInspector]
#endif
        [UnityEngine.SerializeField] protected T value;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        /// <summary>
        /// Gets or sets the current value. Setting a different value raises <see cref="OnChanged"/>.
        /// </summary>
        public T Value
        {
            get => value;
            set
            {
                // Use type-appropriate equality (handles refs, structs, and enums)
                if (EqualityComparer<T>.Default.Equals(this.value, value))
                    return;

                T oldValue = this.value;
                this.value = value;
                OnChanged?.Invoke(this, oldValue, value);
            }
        }

        /// <summary>
        /// Occurs after <see cref="Value"/> is changed to a different value. Parameters are (sender, oldValue, newValue).
        /// </summary>
        public event Action<IObservable<T>, T, T> OnChanged;

        /// <summary>
        /// Initializes a new instance with the default value.
        /// </summary>
        public Observable()
        {
        }

        /// <summary>
        /// Initializes a new instance with the specified value.
        /// </summary>
        /// <param name="value">The initial value.</param>
        public Observable(T value)
        {
            this.value = value;
        }

        /// <summary>
        /// Sets the value without raising <see cref="OnChanged"/>.
        /// </summary>
        /// <param name="valueToSet">The value to set.</param>
        public void SilentSet(T valueToSet)
        {
            value = valueToSet;
        }

        /// <summary>
        /// Implicitly constructs an <see cref="Observable{T}"/> from a value of type <typeparamref name="T"/>.
        /// </summary>
        public static implicit operator Observable<T>(T v)
        {
            return new Observable<T>(v);
        }

        /// <summary>
        /// Explicitly converts an <see cref="Observable{T}"/> to its underlying value.
        /// </summary>
        public static explicit operator T(Observable<T> o)
        {
            return o.value;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return value?.ToString() ?? string.Empty;
        }

        // -----------------------
        // Equality and Hash Code
        // -----------------------

        /// <inheritdoc/>
        public bool Equals(Observable<T> other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null) return false;
            return EqualityComparer<T>.Default.Equals(value, other.value);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is Observable<T> otherObs) return Equals(otherObs);
            if (obj is T otherValue) return EqualityComparer<T>.Default.Equals(value, otherValue);
            return false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return EqualityComparer<T>.Default.GetHashCode(value);
        }

        /// <summary>
        /// Determines whether two observables are equal.
        /// </summary>
        public static bool operator ==(Observable<T> a, Observable<T> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.Equals(b);
        }

        /// <summary>
        /// Determines whether two observables are not equal.
        /// </summary>
        public static bool operator !=(Observable<T> a, Observable<T> b)
        {
            return !(a == b);
        }

        // ---------------
        // Comparisons
        // ---------------

        /// <summary>
        /// Compares this instance with another object for ordering.
        /// Uses Comparer<T>.Default, which requires T to be comparable at runtime.
        /// </summary>
        public int CompareTo(object obj)
        {
            if (obj is null) return 1; // any instance > null
            if (obj is Observable<T> otherObs) return CompareTo(otherObs);
            if (obj is T otherValue) return Comparer<T>.Default.Compare(value, otherValue);

            throw new ArgumentException($"Object must be of type {typeof(Observable<T>)} or {typeof(T)}");
        }

        /// <summary>
        /// Compares this instance with another observable of the same type.
        /// Uses Comparer<T>.Default, which requires T to be comparable at runtime.
        /// </summary>
        public int CompareTo(Observable<T> other)
        {
            if (other is null) return 1;
            return Comparer<T>.Default.Compare(value, other.value);
        }

        // ---------------
        // IConvertible (dynamic delegation)
        // ---------------

        /// <summary>
        /// Gets the underlying IConvertible if available; otherwise throws.
        /// </summary>
        private IConvertible ConvertibleOrThrow()
        {
            if (value is IConvertible c) return c;
            throw new InvalidCastException(
                $"Underlying type {typeof(T)} does not implement IConvertible.");
        }

        TypeCode IConvertible.GetTypeCode()
        {
            return ConvertibleOrThrow().GetTypeCode();
        }

        bool IConvertible.ToBoolean(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToBoolean(provider);
        }

        byte IConvertible.ToByte(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToByte(provider);
        }

        char IConvertible.ToChar(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToChar(provider);
        }

        DateTime IConvertible.ToDateTime(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToDateTime(provider);
        }

        decimal IConvertible.ToDecimal(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToDecimal(provider);
        }

        double IConvertible.ToDouble(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToDouble(provider);
        }

        short IConvertible.ToInt16(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToInt16(provider);
        }

        int IConvertible.ToInt32(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToInt32(provider);
        }

        long IConvertible.ToInt64(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToInt64(provider);
        }

        sbyte IConvertible.ToSByte(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToSByte(provider);
        }

        float IConvertible.ToSingle(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToSingle(provider);
        }

        string IConvertible.ToString(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToString(provider);
        }

        object IConvertible.ToType(Type conversionType, IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToType(conversionType, provider);
        }

        ushort IConvertible.ToUInt16(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToUInt16(provider);
        }

        uint IConvertible.ToUInt32(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToUInt32(provider);
        }

        ulong IConvertible.ToUInt64(IFormatProvider provider)
        {
            return ConvertibleOrThrow().ToUInt64(provider);
        }
    }
}
using System;

namespace LegendaryTools
{
    /// <summary>
    /// Provides an observable wrapper around a value of type <typeparamref name="T"/>.
    /// Notifies subscribers when the value changes to a different value.
    /// </summary>
    /// <typeparam name="T">
    /// The underlying value type. Must support equality and comparison to
    /// allow efficient change detection and comparisons.
    /// </typeparam>
    [Serializable]
    public class Observable<T> : IObservable<T>, IEquatable<Observable<T>>, IComparable<Observable<T>>, IComparable,
        IConvertible
        where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
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
                // Avoid spurious notifications on equal values
                if (this.value is null)
                {
                    if (value is null) return;
                }
                else if (this.value.Equals(value))
                {
                    return;
                }

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
        /// Initializes a new instance of the <see cref="Observable{T}"/> class with the default value.
        /// </summary>
        public Observable()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Observable{T}"/> class with the specified value.
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

        // IComparable (object)
        /// <summary>
        /// Compares this instance with another object for ordering.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns>An integer indicating relative order.</returns>
        /// <exception cref="ArgumentException">Thrown when types are incompatible.</exception>
        public int CompareTo(object obj)
        {
            if (obj is null) return 1; // any instance > null

            if (obj is Observable<T> otherObs)
                // Compare underlying values
                return CompareTo(otherObs);

            if (obj is T otherValue) return value.CompareTo(otherValue);

            throw new ArgumentException($"Object must be of type {typeof(Observable<T>)} or {typeof(T)}");
        }

        // IComparable<Observable<T>>
        /// <summary>
        /// Compares this instance with another observable of the same type.
        /// </summary>
        public int CompareTo(Observable<T> other)
        {
            if (other is null) return 1;
            return value.CompareTo(other.value);
        }

        // IEquatable<Observable<T>>
        /// <summary>
        /// Indicates whether this instance and another observable are equal.
        /// </summary>
        public bool Equals(Observable<T> other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null) return false;

            if (value is null) return other.value is null;
            return value.Equals(other.value);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is Observable<T> otherObs) return Equals(otherObs);
            if (obj is T otherValue)
            {
                if (value is null) return otherValue is null;
                return value.Equals(otherValue);
            }

            return false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return value?.GetHashCode() ?? 0;
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

        // IConvertible pass-throughs
        /// <inheritdoc/>
        public TypeCode GetTypeCode()
        {
            return value.GetTypeCode();
        }

        /// <inheritdoc/>
        public bool ToBoolean(IFormatProvider provider)
        {
            return value.ToBoolean(provider);
        }

        /// <inheritdoc/>
        public byte ToByte(IFormatProvider provider)
        {
            return value.ToByte(provider);
        }

        /// <inheritdoc/>
        public char ToChar(IFormatProvider provider)
        {
            return value.ToChar(provider);
        }

        /// <inheritdoc/>
        public DateTime ToDateTime(IFormatProvider provider)
        {
            return value.ToDateTime(provider);
        }

        /// <inheritdoc/>
        public decimal ToDecimal(IFormatProvider provider)
        {
            return value.ToDecimal(provider);
        }

        /// <inheritdoc/>
        public double ToDouble(IFormatProvider provider)
        {
            return value.ToDouble(provider);
        }

        /// <inheritdoc/>
        public short ToInt16(IFormatProvider provider)
        {
            return value.ToInt16(provider);
        }

        /// <inheritdoc/>
        public int ToInt32(IFormatProvider provider)
        {
            return value.ToInt32(provider);
        }

        /// <inheritdoc/>
        public long ToInt64(IFormatProvider provider)
        {
            return value.ToInt64(provider);
        }

        /// <inheritdoc/>
        public sbyte ToSByte(IFormatProvider provider)
        {
            return value.ToSByte(provider);
        }

        /// <inheritdoc/>
        public float ToSingle(IFormatProvider provider)
        {
            return value.ToSingle(provider);
        }

        /// <inheritdoc/>
        public string ToString(IFormatProvider provider)
        {
            return value.ToString(provider);
        }

        /// <inheritdoc/>
        public object ToType(Type conversionType, IFormatProvider provider)
        {
            return value.ToType(conversionType, provider);
        }

        /// <inheritdoc/>
        public ushort ToUInt16(IFormatProvider provider)
        {
            return value.ToUInt16(provider);
        }

        /// <inheritdoc/>
        public uint ToUInt32(IFormatProvider provider)
        {
            return value.ToUInt32(provider);
        }

        /// <inheritdoc/>
        public ulong ToUInt64(IFormatProvider provider)
        {
            return value.ToUInt64(provider);
        }
    }
}
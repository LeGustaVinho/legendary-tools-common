using System;

namespace LegendaryTools
{
    [Serializable]
    public class Observable<T> : IEquatable<Observable<T>>, IComparable<Observable<T>>, IComparable, IConvertible
        where T : IEquatable<T>, IComparable<T>, IComparable, IConvertible
    {
#if ODIN_INSPECTOR
        [UnityEngine.HideInInspector]
#endif
        [UnityEngine.SerializeField] protected T value;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
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

        public event Action<Observable<T>, T, T> OnChanged;

        public Observable()
        {
        }

        public Observable(T value)
        {
            this.value = value;
        }

        public void SilentSet(T valueToSet)
        {
            value = valueToSet;
        }

        public static implicit operator Observable<T>(T v)
        {
            return new Observable<T>(v);
        }

        public static explicit operator T(Observable<T> o)
        {
            return o.value;
        }

        public override string ToString()
        {
            return value?.ToString() ?? string.Empty;
        }

        // IComparable (object)
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
        public int CompareTo(Observable<T> other)
        {
            if (other is null) return 1;
            return value.CompareTo(other.value);
        }

        // IEquatable<Observable<T>>
        public bool Equals(Observable<T> other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null) return false;

            if (value is null) return other.value is null;
            return value.Equals(other.value);
        }

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

        public override int GetHashCode()
        {
            return value?.GetHashCode() ?? 0;
        }

        public static bool operator ==(Observable<T> a, Observable<T> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.Equals(b);
        }

        public static bool operator !=(Observable<T> a, Observable<T> b)
        {
            return !(a == b);
        }

        // IConvertible pass-throughs
        public TypeCode GetTypeCode()
        {
            return value.GetTypeCode();
        }

        public bool ToBoolean(IFormatProvider provider)
        {
            return value.ToBoolean(provider);
        }

        public byte ToByte(IFormatProvider provider)
        {
            return value.ToByte(provider);
        }

        public char ToChar(IFormatProvider provider)
        {
            return value.ToChar(provider);
        }

        public DateTime ToDateTime(IFormatProvider provider)
        {
            return value.ToDateTime(provider);
        }

        public decimal ToDecimal(IFormatProvider provider)
        {
            return value.ToDecimal(provider);
        }

        public double ToDouble(IFormatProvider provider)
        {
            return value.ToDouble(provider);
        }

        public short ToInt16(IFormatProvider provider)
        {
            return value.ToInt16(provider);
        }

        public int ToInt32(IFormatProvider provider)
        {
            return value.ToInt32(provider);
        }

        public long ToInt64(IFormatProvider provider)
        {
            return value.ToInt64(provider);
        }

        public sbyte ToSByte(IFormatProvider provider)
        {
            return value.ToSByte(provider);
        }

        public float ToSingle(IFormatProvider provider)
        {
            return value.ToSingle(provider);
        }

        public string ToString(IFormatProvider provider)
        {
            return value.ToString(provider);
        }

        public object ToType(Type conversionType, IFormatProvider provider)
        {
            return value.ToType(conversionType, provider);
        }

        public ushort ToUInt16(IFormatProvider provider)
        {
            return value.ToUInt16(provider);
        }

        public uint ToUInt32(IFormatProvider provider)
        {
            return value.ToUInt32(provider);
        }

        public ulong ToUInt64(IFormatProvider provider)
        {
            return value.ToUInt64(provider);
        }
    }
}
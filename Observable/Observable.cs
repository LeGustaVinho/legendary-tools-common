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
                if (value.Equals(this.value))
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
            this.value = valueToSet;
        }
        
        public static implicit operator Observable<T>(T observable)
        {
            return new Observable<T>(observable);
        }
 
        public static explicit operator T(Observable<T> observable)
        {
            return observable.value;
        }
 
        public override string ToString()
        {
            return value.ToString();
        }

        public int CompareTo(object obj)
        {
            return value.CompareTo(obj as Observable<T>);
        }

        public bool Equals(Observable<T> other)
        {
            return other != null && other.value.Equals(value);
        }

        public int CompareTo(Observable<T> other)
        {
            return other.value.CompareTo(other.value);
        }

        public override bool Equals(object other)
        {
            Observable<T> observable = other as Observable<T>;
            return other != null
                   && observable != null && observable.value.Equals(value);
        }
 
        public override int GetHashCode()
        {
            return value.GetHashCode();
        }
        
        public static bool operator ==(Observable<T> a, Observable<T> b)
        {
            return a != null && a.Equals(b);
        }

        public static bool operator !=(Observable<T> a, Observable<T> b)
        {
            return a != null && !a.Equals(b);
        }

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
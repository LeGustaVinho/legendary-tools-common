using System;
using System.Collections.Generic;

namespace LegendaryTools
{
    /// <summary>
    /// Observable built from external getter/setter delegates.
    /// Keeps base storage synchronized to preserve Observable<T> contracts.
    /// </summary>
    /// <typeparam name="T">Underlying value type.</typeparam>
    [Serializable]
    public class AccessorObservable<T> : Observable<T>
    {
        private readonly Func<T> _getter;
        private readonly Action<T> _setter;
        private readonly IEqualityComparer<T> _equalityComparer;
        private readonly IComparer<T> _comparer;

        /// <summary>
        /// Initializes a new instance using the provided getter and setter.
        /// </summary>
        /// <param name="getter">Delegate that returns the current value.</param>
        /// <param name="setter">Delegate that sets the value.</param>
        /// <param name="equalityComparer">Optional equality comparer. Defaults to EqualityComparer&lt;T&gt;.Default.</param>
        /// <param name="comparer">Optional comparer. Defaults to Comparer&lt;T&gt;.Default.</param>
        public AccessorObservable(
            Func<T> getter,
            Action<T> setter,
            IEqualityComparer<T> equalityComparer = null,
            IComparer<T> comparer = null)
        {
            _getter = getter ?? throw new ArgumentNullException(nameof(getter));
            _setter = setter ?? throw new ArgumentNullException(nameof(setter));
            _equalityComparer = equalityComparer ?? EqualityComparer<T>.Default;
            _comparer = comparer ?? Comparer<T>.Default;

            // Bootstrap base storage to keep inherited behaviors consistent.
            value = _getter();
        }

        /// <summary>
        /// Gets or sets the current value via the provided delegates.
        /// Setting a different value raises OnChanged.
        /// </summary>
        public override T Value
        {
            get
            {
                // Sync base storage so inherited members (e.g., IConvertible) remain correct.
                value = _getter();
                return value;
            }
            set
            {
                // Compare using the configured equality comparer.
                T oldValue = Value; // also syncs base.value
                if (_equalityComparer.Equals(oldValue, value))
                    return;

                // Set via delegate and sync base storage.
                _setter(value);
                this.value = _getter();

                // Notify after successful change.
                RaiseOnChanged(oldValue, value);
            }
        }

        /// <summary>
        /// Sets the value without raising OnChanged.
        /// </summary>
        /// <param name="valueToSet">The value to set.</param>
        public override void SilentSet(T valueToSet)
        {
            _setter(valueToSet);
            value = _getter();
        }

        /// <summary>
        /// Compares this instance with another object for ordering.
        /// Uses the configured comparer (defaults to Comparer&lt;T&gt;.Default).
        /// </summary>
        public override int CompareTo(object obj)
        {
            if (obj is null) return 1;
            if (obj is AccessorObservable<T> otherAccessor)
                return _comparer.Compare(Value, otherAccessor.Value);
            if (obj is Observable<T> otherObs)
                return _comparer.Compare(Value, otherObs.Value);
            if (obj is T otherValue)
                return _comparer.Compare(Value, otherValue);

            throw new ArgumentException($"Object must be of type {typeof(Observable<T>)} or {typeof(T)}");
        }

        /// <summary>
        /// Compares this instance with another observable of the same type.
        /// Uses the configured comparer (defaults to Comparer&lt;T&gt;.Default).
        /// </summary>
        public override int CompareTo(Observable<T> other)
        {
            if (other is null) return 1;
            return _comparer.Compare(Value, other.Value);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            T v = Value; // ensure sync/read via getter
            return v?.ToString() ?? string.Empty;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;

            if (obj is AccessorObservable<T> otherAcc)
                return _equalityComparer.Equals(Value, otherAcc.Value);

            if (obj is Observable<T> otherObs)
                return _equalityComparer.Equals(Value, otherObs.Value);

            if (obj is T otherVal)
                return _equalityComparer.Equals(Value, otherVal);

            return false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return _equalityComparer.GetHashCode(Value);
        }

        /// <summary>
        /// Determines whether two observables are equal.
        /// </summary>
        public static bool operator ==(AccessorObservable<T> a, AccessorObservable<T> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.Equals(b);
        }

        /// <summary>
        /// Determines whether two observables are not equal.
        /// </summary>
        public static bool operator !=(AccessorObservable<T> a, AccessorObservable<T> b)
        {
            return !(a == b);
        }
    }
}
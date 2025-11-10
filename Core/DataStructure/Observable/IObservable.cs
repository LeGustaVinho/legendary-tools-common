using System;

namespace LegendaryTools
{
    /// <summary>
    /// Represents an observable value that raises change notifications when modified.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    public interface IObservable<T>
    {
        /// <summary>
        /// Gets or sets the current value. Setting a different value raises <see cref="OnChanged"/>.
        /// </summary>
        T Value { get; set; }

        /// <summary>
        /// Occurs when <see cref="Value"/> changes to a different value.
        /// </summary>
        event Action<IObservable<T>, T, T> OnChanged;

        /// <summary>
        /// Sets the value without raising <see cref="OnChanged"/>.
        /// </summary>
        /// <param name="valueToSet">The value to set.</param>
        void SilentSet(T valueToSet);
    }
}
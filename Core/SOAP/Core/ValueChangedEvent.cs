using System;
using UnityEngine.Events;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Invoked when a variable's value changes. Provides (oldValue, newValue).
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    [Serializable]
    public class ValueChangedEvent<T> : UnityEvent<T, T>
    {
    }
}
using System;
using UnityEngine.Events;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class BindingValueChangedEvent : UnityEvent<object, object>
    {
    }
}

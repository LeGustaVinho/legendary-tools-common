using System;
using UnityEngine;
using UnityEngine.Events;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class ViewDataEventAction
    {
        [SerializeField] private EventActionArgumentMode argumentMode;
        [SerializeField] private UnityEvent onInvoke = new UnityEvent();
        [SerializeField] private BindingValueEvent onValue = new BindingValueEvent();
        [SerializeField] private BindingValueChangedEvent onValueChanged = new BindingValueChangedEvent();

        public EventActionArgumentMode ArgumentMode => argumentMode;

        public void Invoke(object oldValue, object newValue)
        {
            switch (argumentMode)
            {
                case EventActionArgumentMode.None:
                    onInvoke.Invoke();
                    break;

                case EventActionArgumentMode.OldValue:
                    onValue.Invoke(oldValue);
                    break;

                case EventActionArgumentMode.NewValue:
                    onValue.Invoke(newValue);
                    break;

                case EventActionArgumentMode.OldAndNewValue:
                    onValueChanged.Invoke(oldValue, newValue);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}

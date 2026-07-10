using System;
using UnityEngine;
using UnityEngine.Events;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class EventBindingAction
    {
        [SerializeField] private EventBindingActionParameterMode parameterMode;
        [SerializeField] private UnityEvent eventWithoutParameters = new UnityEvent();
        [SerializeField] private BindingObjectUnityEvent oldValueEvent = new BindingObjectUnityEvent();
        [SerializeField] private BindingObjectUnityEvent newValueEvent = new BindingObjectUnityEvent();
        [SerializeField] private BindingObjectPairUnityEvent oldAndNewValuesEvent = new BindingObjectPairUnityEvent();

        public EventBindingActionParameterMode ParameterMode => parameterMode;

        public void Invoke(object oldValue, object newValue)
        {
            switch (parameterMode)
            {
                case EventBindingActionParameterMode.None:
                    eventWithoutParameters.Invoke();
                    break;

                case EventBindingActionParameterMode.OldValue:
                    oldValueEvent.Invoke(oldValue);
                    break;

                case EventBindingActionParameterMode.NewValue:
                    newValueEvent.Invoke(newValue);
                    break;

                case EventBindingActionParameterMode.OldAndNewValues:
                    oldAndNewValuesEvent.Invoke(oldValue, newValue);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}

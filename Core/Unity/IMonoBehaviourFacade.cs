using System;
using System.Collections;
using UnityEngine;

namespace LegendaryTools
{
    public interface IMonoBehaviourFacade
    {
        event Action OnUpdate;
        event Action OnFixedUpdate;
        event Action OnLateUpdate;
        event Action<bool> OnApplicationPaused;
        event Action<bool> OnApplicationFocused;
        void Execute(Action action);
        Coroutine StartRoutine(IEnumerator routine);
        void StopRoutine(IEnumerator routine);
        void StopRoutine(Coroutine routine);
    }
}
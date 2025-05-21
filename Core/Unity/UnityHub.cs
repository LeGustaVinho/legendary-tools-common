using System;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;

namespace LegendaryTools
{
    [SingletonBehaviour(true, true, true)]
    public class UnityHub : SingletonBehaviour<UnityHub>, IUnityHub
    {
        public event Action OnUpdate;
        public event Action OnFixedUpdate;
        public event Action OnLateUpdate;
        public event Action<bool> OnApplicationPaused;
        public event Action<bool> OnApplicationFocused;
        public event Action OnApplicationQuitted;
        
        private readonly ConcurrentQueue<Action> actionQueue = new ConcurrentQueue<Action>();

        public void Execute(Action action)
        { 
            actionQueue.Enqueue(action);
        }
        
        public Coroutine StartRoutine(IEnumerator routine)
        {
            return StartCoroutine(routine);
        }
        
        public void StopRoutine(IEnumerator routine)
        {
            StopCoroutine(routine);
        }
        
        public void StopRoutine(Coroutine routine)
        {
            StopCoroutine(routine);
        }

        private void Update()
        {
            if (actionQueue.Count > 0)
            {
                while (actionQueue.TryDequeue(out Action action))
                {
                    action.Invoke();
                }
            }

            OnUpdate?.Invoke();
        }
        
        private void FixedUpdate()
        {
            OnFixedUpdate?.Invoke();
        }
        
        private void LateUpdate()
        {
            OnLateUpdate?.Invoke();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            OnApplicationFocused?.Invoke(hasFocus);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            OnApplicationPaused?.Invoke(pauseStatus);
        }
        
        void OnApplicationQuit()
        {
            OnApplicationQuitted?.Invoke();
        }

        private void OnDisable()
        {
#if !UNITY_EDITOR
            Debug.LogError($"{nameof(MonoBehaviourFacade)} should not be disabled.");
#endif
        }

        private void OnDestroy()
        {
#if !UNITY_EDITOR
            Debug.LogError($"{nameof(MonoBehaviourFacade)} should not be destroyed.");
#endif
        }
    }
}
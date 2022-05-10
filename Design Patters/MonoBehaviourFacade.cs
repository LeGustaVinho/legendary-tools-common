using System;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;

namespace LegendaryTools
{
    [SingletonBehaviourAttribute(true, true, true)]
    public class MonoBehaviourFacade : SingletonBehaviour<MonoBehaviourFacade>
    {
        public event Action OnUpdate;
        public event Action OnFixedUpdate;
        public event Action OnLateUpdate;
        
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

        private void OnDisable()
        {
            Debug.LogError($"{nameof(MonoBehaviourFacade)} cannot be disabled.");
            gameObject.SetActive(true);
        }
    }
}
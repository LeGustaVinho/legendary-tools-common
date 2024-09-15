using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public interface IProximityDetector<T> where T : MonoBehaviour, ProximityDetector<T>.IProximityDetectable
    {
        event Action<T> OnEntered;
        event Action<T> OnExited;

        List<T> OverlappingActors { get; }

        void ReportAsDestroyed(T actor, bool shouldCallTriggerExit = false);
    }

    [RequireComponent(typeof(Collider))]
    public class ProximityDetector<T> : MonoBehaviour, IProximityDetector<T> 
        where T : MonoBehaviour, ProximityDetector<T>.IProximityDetectable
    {
        public interface IProximityDetectable
        {
            public event Action<T> OnDestroyed;
        }
        
        public event Action<T> OnEntered;
        public event Action<T> OnExited;
        
        private readonly HashSet<T> overlappingActors = new HashSet<T>();
        
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public List<T> OverlappingActors
        {
            get
            {
                foreach (T actor in overlappingActors)
                {
                    if (actor == null)
                    {
                        overlappingActors.Remove(actor);
                    }
                }

                return new List<T>(overlappingActors);
            }
        }
        
        public void ReportAsDestroyed(T actor, bool shouldCallTriggerExit = false)
        {
            if (overlappingActors.Contains(actor))
            {
                overlappingActors.Remove(actor);
                if (shouldCallTriggerExit)
                {
                    OnExitDetected(actor);
                    OnExited?.Invoke(actor);
                }
            }
        }
        
        protected virtual void OnTriggerEnter(Collider other)
        {
            T actor = other.GetComponent<T>();
            if (actor)
            {
                overlappingActors.Add(actor);
                OnEnterDetected(actor);
                OnEntered?.Invoke(actor);

                if (actor is IProximityDetectable proximityDetectable)
                {
                    proximityDetectable.OnDestroyed += ForceExitNotification;
                }
            }
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            T actor = other.GetComponent<T>();
            if (actor)
            {
                overlappingActors.Remove(actor);
                OnExitDetected(actor);
                OnExited?.Invoke(actor);
            }
        }
        
        protected  virtual void OnValidate()
        {
            Collider colliderComponent = GetComponent<Collider>();
            if (colliderComponent != null && !colliderComponent.isTrigger)
            {
                Debug.LogError("[ProximityDetector] Collider is not a trigger", this);
            }
        }

        protected virtual void OnEnterDetected(T actor)
        {
            
        }
        
        protected virtual void OnExitDetected(T actor)
        {
            
        }
        
        private void ForceExitNotification(T actor)
        {
            if (actor is IProximityDetectable proximityDetectable)
            {
                proximityDetectable.OnDestroyed -= ForceExitNotification;
            }
            ReportAsDestroyed(actor, true);
        }
    }
}
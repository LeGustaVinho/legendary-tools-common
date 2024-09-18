using System;
using UnityEngine;

namespace LegendaryTools
{
    [Serializable]
    public class UniqueBehaviourReference
    {
        [SerializeField] protected string uniqueBehaviourId;
        [SerializeField] protected string gameObjectName;
        [SerializeField] protected int sceneId;
        [SerializeField] protected string sceneName;
        
        public UniqueBehaviour Value
        {
            get => UniqueBehaviour.TryGetValue(uniqueBehaviourId, out UniqueBehaviour uniqueBehaviour) ? uniqueBehaviour : null;
            set => uniqueBehaviourId = value.Guid;
        }
    }
    
    [Serializable]
    public class UniqueBehaviourReference<T> : UniqueBehaviourReference
        where T : Component
    {
        public new T Value
        {
            get => UniqueBehaviour.TryGetValue(uniqueBehaviourId, out UniqueBehaviour uniqueBehaviour) ? uniqueBehaviour.GetComponent<T>() : null;
            set => uniqueBehaviourId = value.GetComponent<UniqueBehaviour>().Guid;
        }
    }
}
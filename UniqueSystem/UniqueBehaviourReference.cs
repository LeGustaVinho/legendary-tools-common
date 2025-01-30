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
            get => UniqueBehaviour.TryGetValue(uniqueBehaviourId, out UniqueBehaviour uniqueBehaviour)
                ? uniqueBehaviour
                : null;
            set
            {
                if (value == null)
                {
                    uniqueBehaviourId = string.Empty;
                }
                else
                {
                    uniqueBehaviourId = value.Guid;
                }
            }
        }
    }
    
    [Serializable]
    public class UniqueBehaviourReference<T> : UniqueBehaviourReference
        where T : Component
    {
        public new T Value
        {
            get
            {
                if (UniqueBehaviour.TryGetValue(uniqueBehaviourId, out UniqueBehaviour uniqueBehaviour))
                {
                    return uniqueBehaviour != null 
                        ? uniqueBehaviour.GetComponent<T>() 
                        : null;
                }
                return null;
            }
            set
            {
                if (value == null)
                {
                    uniqueBehaviourId = string.Empty;
                }
                else
                {
                    var unique = value.GetComponent<UniqueBehaviour>();
                    if (unique != null)
                        uniqueBehaviourId = unique.Guid;
                    else
                        uniqueBehaviourId = string.Empty;
                }
            }
        }
    }
}
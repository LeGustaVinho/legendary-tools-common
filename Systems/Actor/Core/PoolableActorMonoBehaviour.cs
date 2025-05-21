using UnityEngine;

namespace LegendaryTools.Actor
{
    public class PoolableActorMonoBehaviour : ActorMonoBehaviour, IPoolableGameObject
    {
        public virtual void OnConstruct()
        {
        }

        public virtual void OnCreate()
        {
            
        }

        public virtual void OnRecycle()
        {
            OnDestroy();
        }

        public virtual void OnConstruct(Vector3 position, Quaternion rotation, Transform parent)
        {
        }

        public virtual void OnCreate(Vector3 position, Quaternion rotation, Transform parent)
        {
            Start();
        }
    }
}
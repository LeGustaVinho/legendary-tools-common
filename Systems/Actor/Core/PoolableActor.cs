using UnityEngine;

namespace LegendaryTools.Actor
{
    public class PoolableActor<TBehaviour> : Actor<TBehaviour>
        where TBehaviour : PoolableActorMonoBehaviour
    {
        private static GameObject EmptyGameObject = new GameObject("EmptyGameObject");
        
        public PoolableActor() : base()
        {
        }

        public PoolableActor(GameObject prefab = null, string name = "") : base(prefab, name)
        {
        }
        
        protected override GameObject CreateGameObject(string name = "", GameObject prefab = null)
        {
            if (prefab == null)
            {
                return Pool.Instantiate(EmptyGameObject);
            }

            return Pool.Instantiate(prefab);
        }

        protected override void DestroyGameObject(ActorMonoBehaviour actorBehaviour)
        {
            Pool.Destroy(actorBehaviour);
        }
    }
}
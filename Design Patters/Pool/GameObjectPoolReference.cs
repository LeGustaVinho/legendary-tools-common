using UnityEngine;

namespace LegendaryTools
{
    public class GameObjectPoolReference : MonoBehaviour, IPoolable
    {
        [Header("GameObjectPoolReference")]
        public int PrefabID;
        
        public virtual void OnConstruct()
        {
        }

        public virtual void OnCreate()
        {
        }

        public virtual void OnRecycle()
        {
        }
    }
}
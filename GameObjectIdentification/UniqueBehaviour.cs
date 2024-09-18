using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public class UniqueBehaviour : 
#if ODIN_INSPECTOR && UNIQUE_BEHAVIOUR_SERIALIZED_MONOBEHAVIOUR
        Sirenix.OdinInspector.SerializedMonoBehaviour
#else
        MonoBehaviour
#endif
    {
        [SerializeField] private string guid;
        public string Guid => guid;
        private static readonly Dictionary<string, UniqueBehaviour> GameObjectsByGuid = new Dictionary<string, UniqueBehaviour>();
        
        [ContextMenu("Assign New Guid")]
#if ODIN_INSPECTOR 
        [Sirenix.OdinInspector.Button]
#endif
        public void AssignNewGuid()
        {
            string newGuid;
            do
            {
                newGuid = System.Guid.NewGuid().ToString();
            } while (GameObjectsByGuid.ContainsKey(newGuid));
            
            guid = newGuid;
            GameObjectsByGuid.Add(guid, this);
            this.SetDirty();
        }

        public static bool TryGetValue(string guid, out UniqueBehaviour uniqueBehaviour)
        {
            return GameObjectsByGuid.TryGetValue(guid, out uniqueBehaviour);
        }
        
        public static bool Contains(string guid)
        {
            return GameObjectsByGuid.ContainsKey(guid);
        }
        
        protected virtual void Awake()
        {
            if (string.IsNullOrEmpty(guid) && !gameObject.IsPrefab())
            {
                AssignNewGuid();
            }

            if (GameObjectsByGuid.TryGetValue(guid, out UniqueBehaviour uniqueBehaviour))
            {
                if (uniqueBehaviour != this)
                    OnGuidCollisionDetected(uniqueBehaviour);
            }
            else
                GameObjectsByGuid.Add(guid, this);
        }
        
#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            bool allowAssignGuid = !UnityExtension.IsInPrefabMode();
            
            if (gameObject.IsPrefab() || this.IsPrefab())
            {
                allowAssignGuid = false;
            }
            
            if (string.IsNullOrEmpty(guid))
            {
                if(allowAssignGuid)
                    AssignNewGuid();
            }
            else
            {
                if (GameObjectsByGuid.TryGetValue(guid, out UniqueBehaviour uniqueBehaviour))
                {
                    //Unity does an assemblyReload leaving the Dictionary entries null
                    if (uniqueBehaviour == null)
                    {
                        UniqueBehaviour[] allUniqueBehaviours = FindObjectsByType<UniqueBehaviour>(FindObjectsInactive.Include,
                            FindObjectsSortMode.None);
                        foreach (UniqueBehaviour behaviour in allUniqueBehaviours)
                        {
                            GameObjectsByGuid.AddOrUpdate(guid, behaviour);
                            if (guid == behaviour.Guid) uniqueBehaviour = behaviour;
                        }
                    }
                    
                    if (uniqueBehaviour != this)
                    {
                        if (allowAssignGuid)
                            OnGuidCollisionDetected(uniqueBehaviour);
                    }
                }
                else
                    GameObjectsByGuid.Add(guid, this);
            }
        }

        private void OnGuidCollisionDetected(UniqueBehaviour uniqueBehaviour)
        {
            Debug.Log($"[UniqueBehaviour:OnValidate] Guid {guid} collision detected with {gameObject.name} and {uniqueBehaviour.name}, assigning new Guid.");
            AssignNewGuid();
        }
#endif
    }
}
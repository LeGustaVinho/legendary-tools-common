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
        private static readonly Dictionary<string, UniqueBehaviour> allGuids = new Dictionary<string, UniqueBehaviour>();
        
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
            } while (allGuids.ContainsKey(newGuid));
            
            guid = newGuid;
            allGuids.Add(guid, this);
            this.SetDirty();
        }

        public static bool TryGetValue(string guid, out UniqueBehaviour uniqueBehaviour)
        {
            return allGuids.TryGetValue(guid, out uniqueBehaviour);
        }
        
        public static bool Contains(string guid)
        {
            return allGuids.ContainsKey(guid);
        }
        
        protected virtual void Awake()
        {
            if (string.IsNullOrEmpty(guid) && !gameObject.IsPrefab())
            {
                AssignNewGuid();
            }
            allGuids.AddOrUpdate(guid, this);
        }
        
        protected virtual void OnValidate()
        {
            bool allowAssignGuid = !UnityExtension.IsInPrefabMode();
            
            if (gameObject.IsPrefab() || this.IsPrefab())
            {
                allowAssignGuid = false;
            }
            
            if (string.IsNullOrEmpty(guid) || allGuids.ContainsKey(guid))
            {
                if(allowAssignGuid)
                    AssignNewGuid();
            }
            else
            {
                allGuids.Add(guid, this);
            }
        }
    }
}
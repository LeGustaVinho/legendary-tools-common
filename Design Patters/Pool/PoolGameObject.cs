using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public class PoolGameObject : PoolObject<GameObject>
    {
        private readonly GameObject Original;

        public PoolGameObject(GameObject original)
        {
            Instance = this;
            Original = original;
        }

        protected override GameObject NewObject()
        {
            GameObject obj = GameObject.Instantiate(Original);
            obj.name = Original.name + " # " + (ActiveInstances.Count + InactiveInstances.Count);
            GameObjectPoolReference reference = obj.AddComponent<GameObjectPoolReference>();
            reference.PrefabID = Original.GetHashCode();
            NotifyOnConstruct(obj);
            return obj;
        }

        public override GameObject CreateAs()
        {
            GameObject obj =  base.CreateAs();
            obj.SetActive(true);
            NotifyOnCreate(obj);
            return obj;
        }

        public GameObject Create(Transform parent)
        {
            GameObject obj = CreateAs();
            obj.transform.SetParent(parent);
            return obj;
        }
        
        public GameObject Create(Vector3 position, Quaternion rotation)
        {
            GameObject obj = CreateAs();
            Transform objTransform = obj.transform;
            objTransform.position = position;
            objTransform.rotation = rotation;
            return obj;
        }
        
        public GameObject Create(Vector3 position, Quaternion rotation, Transform parent)
        {
            GameObject obj = CreateAs();
            Transform objTransform = obj.transform;
            objTransform.SetParent(parent);
            objTransform.position = position;
            objTransform.rotation = rotation;
            return obj;
        }

        public override void Recycle(GameObject instance)
        {
            instance.SetActive(false);
            instance.transform.SetParent(null);
            NotifyOnRecycle(instance);
            
            base.Recycle(instance);
        }

        public override void Clear()
        {
            List<GameObject> instances = new List<GameObject>(ActiveInstances.Count + InactiveInstances.Count);
            instances.AddRange(ActiveInstances);
            instances.AddRange(InactiveInstances);
            
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i] != null)
                {
#if UNITY_EDITOR
                    Object.DestroyImmediate(instances[i]);
#else
                    Object.Destroy(instances[i]);
#endif
                }
            }
            
            base.Clear();
        }

        private void NotifyOnConstruct(GameObject obj)
        {
            Component[] comps = obj.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] is IPoolable)
                {
                    (comps[i] as IPoolable).OnConstruct();
                }
            }
        }

        private void NotifyOnCreate(GameObject obj)
        {
            Component[] comps = obj.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] is IPoolable)
                {
                    (comps[i] as IPoolable).OnCreate();
                }
            }
        }

        private void NotifyOnRecycle(GameObject obj)
        {
            Component[] comps = obj.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] is IPoolable)
                {
                    (comps[i] as IPoolable).OnRecycle();
                }
            }
        }
    }
}
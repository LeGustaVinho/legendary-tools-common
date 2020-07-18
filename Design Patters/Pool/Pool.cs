using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public class Pool
    {
        private static readonly Dictionary<int, PoolGameObject> GameObjectPools =
            new Dictionary<int, PoolGameObject>();

        public static T Instantiate<T>(T prefab)
            where T : UnityEngine.Object
        {
            if (prefab == null)
            {
                Debug.LogError("[Pool:Instantiate()] -> Prefab cant be null.");
                return null;
            }

            if (typeof(T).IsSameOrSubclass(typeof(Component)))
            {
                PoolGameObject pool = CreateOrGetPool((prefab as Component).gameObject);
                return pool.CreateAs().GetComponent<T>();
            }

            if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                PoolGameObject pool = CreateOrGetPool(prefab as GameObject);
                return pool.Create() as T;
            }

            return null;
        }

        public static T Instantiate<T>(T prefab, Transform parent)
            where T : UnityEngine.Object
        {
            if (prefab == null)
            {
                Debug.LogError("[Pool:Instantiate()] -> Prefab cant be null.");
                return null;
            }

            if (typeof(T).IsSameOrSubclass(typeof(Component)))
            {
                PoolGameObject pool = CreateOrGetPool((prefab as Component).gameObject);
                return pool.Create(parent).GetComponent<T>();
            }

            if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                PoolGameObject pool = CreateOrGetPool(prefab as GameObject);
                return pool.Create(parent) as T;
            }

            return null;
        }

        public static T Instantiate<T>(T prefab, Vector3 position, Quaternion rotation)
            where T : UnityEngine.Object
        {
            if (prefab == null)
            {
                Debug.LogError("[Pool:Instantiate()] -> Prefab cant be null.");
                return null;
            }

            if (typeof(T).IsSameOrSubclass(typeof(Component)))
            {
                PoolGameObject pool = CreateOrGetPool((prefab as Component).gameObject);
                return pool.Create(position, rotation).GetComponent<T>();
            }

            if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                PoolGameObject pool = CreateOrGetPool(prefab as GameObject);
                return pool.Create(position, rotation) as T;
            }

            return null;
        }

        public static T Instantiate<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent)
            where T : UnityEngine.Object
        {
            if (prefab == null)
            {
                Debug.LogError("[Pool:Instantiate()] -> Prefab cant be null.");
                return null;
            }

            if (typeof(T).IsSameOrSubclass(typeof(Component)))
            {
                PoolGameObject pool = CreateOrGetPool((prefab as Component).gameObject);
                return pool.Create(position, rotation, parent).GetComponent<T>();
            }

            if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                PoolGameObject pool = CreateOrGetPool(prefab as GameObject);
                return pool.Create(position, rotation, parent) as T;
            }

            return null;
        }

        public static void Destroy<T>(T instance)
            where T : UnityEngine.Object
        {
            if (instance == null)
            {
                Debug.LogError("[Pool:Instantiate()] -> Instance cant be null.");
                return;
            }

            if (typeof(T).IsSameOrSubclass(typeof(Component)))
            {
                GameObject go = (instance as Component)?.gameObject;
                CreateOrGetPool(go).Recycle(go);
            }
            else if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                GameObject go = instance as GameObject;
                CreateOrGetPool(go).Recycle(go);
            }
        }

        public static T Construct<T>(T instance)
            where T : class
        {
            return CreateOrGetPool<T>().CreateAs();
        }
        
        public static void Dispose<T>(T instance)
            where T : class
        {
            CreateOrGetPool<T>().Recycle(instance);
        }

        public static void FillInstances<T>(List<T> instances)
            where T : class
        {
            for (int i = 0; i < instances.Count; i++)
            {
                AddInstance(instances[i]);
            }
        }
        
        public static void AddInstance<T>(T instance)
            where T : class
        {
            if (instance == null)
            {
                Debug.LogError("[Pool:AddInstance()] -> Instance cant be null.");
                return;
            }

            if (typeof(T).IsSameOrSubclass(typeof(Component)))
            {
                PoolGameObject pool = CreateOrGetPool((instance as Component).gameObject);
                pool.AddInstance(instance);
            }
            else if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                PoolGameObject pool = CreateOrGetPool(instance as GameObject);
                pool.AddInstance(instance);
            }
            else
            {
                CreateOrGetPool<T>().AddInstance(instance);
            }
        }
        
        public static void ClearPool<T>(T instance)
            where T : class
        {
            if (instance == null)
            {
                Debug.LogError("[Pool:ClearPool()] -> Instance cant be null.");
                return;
            }

            if (typeof(T).IsSameOrSubclass(typeof(Component)))
            {
                PoolGameObject pool = CreateOrGetPool((instance as Component).gameObject);
                pool.Clear();
            }
            else if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                PoolGameObject pool = CreateOrGetPool(instance as GameObject);
                pool.Clear();
            }
            else
            {
                CreateOrGetPool<T>().Clear();
            }
        }
        
        public static void ClearAll()
        {
            PoolObject.ClearAllPools();
        }
        
        private static PoolGameObject CreateOrGetPool(GameObject gameObject)
        {
            if (gameObject == null)
            {
                Debug.LogError("[Pool:CreateOrGetPool()] -> GameObject cant be null.");
                return null;
            }

            int objectId = gameObject.GetHashCode();
            GameObjectPoolReference poolReferenceComp = gameObject.GetComponent<GameObjectPoolReference>();
            if (poolReferenceComp != null)
            {
                objectId = poolReferenceComp.PrefabID;
            }
            
            if (GameObjectPools.TryGetValue(objectId, out PoolGameObject gameObjPool))
            {
                return gameObjPool;
            }

            GameObjectPools.Add(objectId, new PoolGameObject(gameObject));
            return GameObjectPools[objectId];
        }

        private static PoolObject<T> CreateOrGetPool<T>()
            where T : class
        {
            return PoolObject<T>.Instance ?? new PoolObject<T>();
        }
    }
}
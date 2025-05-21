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
                return pool.CreateAsFor().GetComponent<T>();
            }

            if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                PoolGameObject pool = CreateOrGetPool(prefab as GameObject);
                return pool.CreateAsFor() as T;
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
                return pool.CreateAsFor(parent).GetComponent<T>();
            }

            if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                PoolGameObject pool = CreateOrGetPool(prefab as GameObject);
                return pool.CreateAsFor(parent) as T;
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
                return pool.CreateAsFor(position, rotation).GetComponent<T>();
            }

            if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                PoolGameObject pool = CreateOrGetPool(prefab as GameObject);
                return pool.CreateAsFor(position, rotation) as T;
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
                return pool.CreateAsFor(position, rotation, parent).GetComponent<T>();
            }

            if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                PoolGameObject pool = CreateOrGetPool(prefab as GameObject);
                return pool.CreateAsFor(position, rotation, parent) as T;
            }

            return null;
        }

        public static void Destroy<T>(T instance)
            where T : UnityEngine.Object
        {
            if (instance == null)
            {
                Debug.LogError("[Pool:Destroy()] -> Instance cant be null.");
                return;
            }

            if (typeof(T).IsSameOrSubclass(typeof(Component)))
            {
                GameObject go = (instance as Component)?.gameObject;
                GetPool(go)?.Recycle(go);
            }
            else if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                GameObject go = instance as GameObject;
                GetPool(go)?.Recycle(go);
            }
        }

        public static T Construct<T>()
            where T : class
        {
            return CreateOrGetPool<T>().Create() as T;
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
                GameObject instanceGameObject = (instance as Component).gameObject;
                PoolGameObject pool = CreateOrGetPool(instanceGameObject);
                pool.AddInstance(instanceGameObject);
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

            int? objectId = null;
            if (typeof(T).IsSameOrSubclass(typeof(Component)))
            {
                PoolGameObject pool = GetPool((instance as Component).gameObject);
                pool?.Clear();
                objectId = GetPrefabId((instance as Component).gameObject);
            }
            else if (typeof(T).IsSameOrSubclass(typeof(GameObject)))
            {
                PoolGameObject pool = GetPool(instance as GameObject);
                pool?.Clear();
                objectId = GetPrefabId(instance as GameObject);
            }
            else
            {
                PoolObject<T>.Instance?.Clear();
            }

            if (objectId != null)
            {
                if (GameObjectPools.ContainsKey(objectId.Value))
                {
                    GameObjectPools.Remove(objectId.Value);
                }
            }
        }
        
        public static void ClearAll()
        {
            PoolObject.ClearAllPools();
            GameObjectPools.Clear();
        }

        public static PoolGameObject GetPool(GameObject gameObject)
        {
            if (gameObject == null)
            {
                Debug.LogError("[Pool:CreateOrGetPool()] -> GameObject cant be null.");
                return null;
            }

            int objectId = GetPrefabId(gameObject);

            if (GameObjectPools.TryGetValue(objectId, out PoolGameObject gameObjPool))
            {
                return gameObjPool;
            }

            return null;
        }
        
        private static PoolGameObject CreateOrGetPool(GameObject gameObject)
        {
            PoolGameObject pool = GetPool(gameObject);

            if (pool == null)
            {
                int objectId = GetPrefabId(gameObject);
                GameObjectPools.Add(objectId, new PoolGameObject(gameObject));
                return GameObjectPools[objectId];
            }

            return pool;
        }

        private static int GetPrefabId(GameObject gameObject)
        {
            int objectId = gameObject.GetHashCode();
            GameObjectPoolReference poolReferenceComp = gameObject.GetComponent<GameObjectPoolReference>();
            if (poolReferenceComp != null)
            {
                objectId = poolReferenceComp.PrefabID;
            }

            return objectId;
        }

        private static PoolObject<T> CreateOrGetPool<T>()
            where T : class
        {
            return PoolObject<T>.Instance ?? new PoolObject<T>();
        }
    }
}
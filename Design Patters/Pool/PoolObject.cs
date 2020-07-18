using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools
{
    public interface IPoolable
    {
        void OnConstruct();

        void OnCreate();

        void OnRecycle();
    }

    public abstract class PoolObject
    {
        public bool CanAutoGenerateInstances { protected set; get; }
        
        private static readonly List<PoolObject> AllPools = new List<PoolObject>();

        protected PoolObject()
        {
            AllPools.Add(this);
        }
        
        public abstract System.Object Create();

        public abstract void Recycle(System.Object instance);
        
        public abstract void Clear();
        public abstract void AddInstance(System.Object instance);

        public static void ClearAllPools()
        {
            for (int i = 0; i < AllPools.Count; i++)
            {
                AllPools[i].Clear();
            }
            
            AllPools.Clear();
        }
    }
    
    public class PoolObject<T> : PoolObject
        where T : class
    {
        public static PoolObject<T> Instance;
        protected readonly List<T> ActiveInstances = new List<T>();
        protected readonly List<T> InactiveInstances = new List<T>();

        public List<T> AllInstances
        {
            get
            {
                List<T> allInstances = new List<T>(ActiveInstances.Count + InactiveInstances.Count);
                allInstances.AddRange(ActiveInstances);
                allInstances.AddRange(InactiveInstances);
                return allInstances;
            }
        }

        public PoolObject() : base()
        {
            Instance = this;
            CanAutoGenerateInstances = typeof(T).HasDefaultConstructor();
        }

        public override System.Object Create()
        {
            return CreateAs();
        }

        public virtual T CreateAs()
        {
            T newObject = default;

            if (InactiveInstances.Count > 0)
            {
                newObject = InactiveInstances[0];
                InactiveInstances.RemoveAt(0);
            }
            else
            {
                if (CanAutoGenerateInstances)
                {
                    newObject = NewObject();

                    if (newObject is IPoolable poolable)
                    {
                        poolable.OnConstruct();
                    }
                }
            }

            ActiveInstances.Add(newObject);

            if (newObject is IPoolable poolable1)
            {
                poolable1.OnCreate();
            }

            return newObject;
        }

        public override void Recycle(object instance)
        {
            Recycle(instance as T);
        }
        
        public virtual void Recycle(T instance)
        {
            if (instance != null)
            {
                if (ActiveInstances.Contains(instance))
                {
                    ActiveInstances.Remove(instance);
                    InactiveInstances.Add(instance);

                    if (instance is IPoolable poolable)
                    {
                        poolable.OnRecycle();
                    }
                }
                else
                {
                    Debug.LogWarning("[PoolObject:Recycle()] -> ActiveInstance does not contains the instance.");
                }
            }
            else
            {
                Debug.LogWarning("[PoolObject:Recycle()] -> Instance cannot be null.");
            }
        }

        protected virtual T NewObject()
        {
            return Activator.CreateInstance<T>();
        }

        public override void Clear()
        {
            InactiveInstances.Clear();
            ActiveInstances.Clear();
        }

        public void FillInstances(List<T> instances)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                AddInstance(instances[i] as T);
            }
        }

        public override void AddInstance(System.Object instance)
        {
            AddInstance(instance as T);
        }
        
        public void AddInstance(T instance)
        {
            InactiveInstances.Add(instance);
        }

        public static void RecycleInstance(T instance)
        {
            Instance?.Recycle(instance);
        }
    }
}
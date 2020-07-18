using UnityEngine;

namespace LegendaryTools
{
    /// <summary>
    /// Defines an Instance operation that lets clients access its unique instance. Instance is a class operation.
    /// Responsible for creating and maintaining its own unique instance.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public abstract class SingletonBehaviour<T> : MonoBehaviour
        where T : MonoBehaviour
    {
        private static T instance;
        public bool ForceSingleInstance;
        public bool IsPersistent;

        public static T Instance
        {
            get
            {
                if (!instance)
                {
                    instance = FindObjectOfType<T>();
                }

                return instance;
            }
        }

        protected virtual void Awake()
        {
            GetInstance();
        }

        protected virtual void Start()
        {
            GetInstance();
        }

        private void GetInstance()
        {
            if (!instance)
            {
                instance = GetComponent<T>();
            }

            if (ForceSingleInstance)
            {
                (instance as SingletonBehaviour<T>).RemoveDuplicateInstances();
            }

            if (IsPersistent)
            {
                DontDestroyOnLoad(this);
            }
        }

        private void RemoveDuplicateInstances()
        {
            T[] clones = FindObjectsOfType<T>();

            if (clones.Length > 1)
            {
                for (int i = 0; i < clones.Length; i++)
                {
                    if (clones[i] != instance)
                    {
                        Destroy(clones[i].gameObject);
                    }
                }
            }
        }
    }
}
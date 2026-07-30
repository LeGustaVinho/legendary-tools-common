using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    internal static class BindingTaskObserver
    {
        private static ObserverBehaviour observer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            observer = null;
        }

        public static void Observe(Task task)
        {
            if (task == null || task.IsCompleted)
            {
                ObserveCompleted(task);
                return;
            }

            EnsureObserver().Add(task);
        }

        private static ObserverBehaviour EnsureObserver()
        {
            if (observer != null)
            {
                return observer;
            }

            var gameObject = new GameObject("ViewDataBinding Task Observer")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            observer = gameObject.AddComponent<ObserverBehaviour>();
            return observer;
        }

        private static void ObserveCompleted(Task task)
        {
            if (task == null || !task.IsCompleted)
            {
                return;
            }

            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private sealed class ObserverBehaviour : MonoBehaviour
        {
            private readonly List<Task> tasks = new List<Task>();

            public void Add(Task task)
            {
                if (!tasks.Contains(task))
                {
                    tasks.Add(task);
                }
            }

            private void Update()
            {
                for (int i = tasks.Count - 1; i >= 0; i--)
                {
                    Task task = tasks[i];
                    if (!task.IsCompleted)
                    {
                        continue;
                    }

                    tasks.RemoveAt(i);
                    ObserveCompleted(task);
                }
            }

            private void OnDestroy()
            {
                observer = null;
                for (int i = 0; i < tasks.Count; i++)
                {
                    Task task = tasks[i];
                    if (task != null && task.IsCompleted)
                    {
                        ObserveCompleted(task);
                    }
                }

                tasks.Clear();
            }
        }
    }
}

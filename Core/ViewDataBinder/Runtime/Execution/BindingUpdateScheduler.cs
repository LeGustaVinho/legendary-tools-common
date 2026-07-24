using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    internal static class BindingUpdateScheduler
    {
        private static SchedulerBehaviour scheduler;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            scheduler = null;
        }

        public static void Register(BindingPollingBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return;
            }

            EnsureScheduler().Register(behaviour);
        }

        public static void Unregister(BindingPollingBehaviour behaviour)
        {
            if (scheduler != null)
            {
                scheduler.Unregister(behaviour);
            }
        }

        private static SchedulerBehaviour EnsureScheduler()
        {
            if (scheduler != null)
            {
                return scheduler;
            }

            var gameObject = new GameObject("ViewDataBinding Update Scheduler")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            Object.DontDestroyOnLoad(gameObject);
            scheduler = gameObject.AddComponent<SchedulerBehaviour>();
            return scheduler;
        }

        private sealed class SchedulerBehaviour : MonoBehaviour
        {
            private readonly List<BindingPollingBehaviour> behaviours =
                new List<BindingPollingBehaviour>();

            public void Register(BindingPollingBehaviour behaviour)
            {
                if (!behaviours.Contains(behaviour))
                {
                    behaviours.Add(behaviour);
                }
            }

            public void Unregister(BindingPollingBehaviour behaviour)
            {
                behaviours.Remove(behaviour);
            }

            private void Update()
            {
                Process(BindingUpdateTiming.Update);
            }

            private void LateUpdate()
            {
                Process(BindingUpdateTiming.LateUpdate);
            }

            private void FixedUpdate()
            {
                Process(BindingUpdateTiming.FixedUpdate);
            }

            private void Process(BindingUpdateTiming timing)
            {
                for (int i = behaviours.Count - 1; i >= 0; i--)
                {
                    BindingPollingBehaviour behaviour = behaviours[i];
                    if (behaviour == null)
                    {
                        behaviours.RemoveAt(i);
                        continue;
                    }

                    behaviour.ProcessScheduledTiming(timing);
                }
            }

            private void OnDestroy()
            {
                scheduler = null;
                behaviours.Clear();
            }
        }
    }
}

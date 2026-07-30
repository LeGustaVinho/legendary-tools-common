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

        public static void Refresh(BindingPollingBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return;
            }

            bool wantsUpdate = behaviour.WantsScheduledTiming(BindingUpdateTiming.Update);
            bool wantsLateUpdate = behaviour.WantsScheduledTiming(BindingUpdateTiming.LateUpdate);
            bool wantsFixedUpdate = behaviour.WantsScheduledTiming(BindingUpdateTiming.FixedUpdate);
            if (scheduler == null && !wantsUpdate && !wantsLateUpdate && !wantsFixedUpdate)
            {
                return;
            }

            EnsureScheduler().SetRegistration(
                behaviour,
                wantsUpdate,
                wantsLateUpdate,
                wantsFixedUpdate);
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
            private readonly List<BindingPollingBehaviour> updateBehaviours =
                new List<BindingPollingBehaviour>();
            private readonly List<BindingPollingBehaviour> lateUpdateBehaviours =
                new List<BindingPollingBehaviour>();
            private readonly List<BindingPollingBehaviour> fixedUpdateBehaviours =
                new List<BindingPollingBehaviour>();

            public void SetRegistration(
                BindingPollingBehaviour behaviour,
                bool wantsUpdate,
                bool wantsLateUpdate,
                bool wantsFixedUpdate)
            {
                SetRegistration(updateBehaviours, behaviour, wantsUpdate);
                SetRegistration(lateUpdateBehaviours, behaviour, wantsLateUpdate);
                SetRegistration(fixedUpdateBehaviours, behaviour, wantsFixedUpdate);
            }

            public void Unregister(BindingPollingBehaviour behaviour)
            {
                updateBehaviours.Remove(behaviour);
                lateUpdateBehaviours.Remove(behaviour);
                fixedUpdateBehaviours.Remove(behaviour);
            }

            private void Update()
            {
                Process(updateBehaviours, BindingUpdateTiming.Update);
            }

            private void LateUpdate()
            {
                Process(lateUpdateBehaviours, BindingUpdateTiming.LateUpdate);
            }

            private void FixedUpdate()
            {
                Process(fixedUpdateBehaviours, BindingUpdateTiming.FixedUpdate);
            }

            private static void SetRegistration(
                List<BindingPollingBehaviour> behaviours,
                BindingPollingBehaviour behaviour,
                bool registered)
            {
                int index = behaviours.IndexOf(behaviour);
                if (registered)
                {
                    if (index < 0)
                    {
                        behaviours.Add(behaviour);
                    }
                }
                else if (index >= 0)
                {
                    behaviours.RemoveAt(index);
                }
            }

            private static void Process(
                List<BindingPollingBehaviour> behaviours,
                BindingUpdateTiming timing)
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
                updateBehaviours.Clear();
                lateUpdateBehaviours.Clear();
                fixedUpdateBehaviours.Clear();
            }
        }
    }
}

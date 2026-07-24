using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    public abstract class BindingPollingBehaviour : MonoBehaviour
    {
        protected virtual void OnEnable()
        {
            PrepareRuntime();
            BindingUpdateScheduler.Register(this);
        }

        protected virtual void Awake()
        {
            ProcessIfRequired(BindingUpdateTiming.Awake);
        }

        protected virtual void Start()
        {
            ProcessIfRequired(BindingUpdateTiming.Start);
        }

        protected virtual void OnDisable()
        {
            BindingUpdateScheduler.Unregister(this);
            ResetRuntimeState();
        }

        protected virtual void PrepareRuntime()
        {
        }

        protected virtual bool HasBindingsForTiming(BindingUpdateTiming timing)
        {
            return true;
        }

        protected virtual bool HasAdditionalScheduledWork(BindingUpdateTiming timing)
        {
            return false;
        }

        protected virtual void AfterScheduledTiming(BindingUpdateTiming timing)
        {
        }

        protected virtual void ResetRuntimeState()
        {
        }

        protected abstract void ProcessBindingTiming(BindingUpdateTiming timing);

        internal void ProcessScheduledTiming(BindingUpdateTiming timing)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            bool hasBindings = HasBindingsForTiming(timing);
            bool hasAdditionalWork = HasAdditionalScheduledWork(timing);
            if (!hasBindings && !hasAdditionalWork)
            {
                return;
            }

            if (hasBindings)
            {
                ProcessBindingTiming(timing);
            }

            AfterScheduledTiming(timing);
        }

        private void ProcessIfRequired(BindingUpdateTiming timing)
        {
            if (HasBindingsForTiming(timing))
            {
                ProcessBindingTiming(timing);
            }
        }
    }
}

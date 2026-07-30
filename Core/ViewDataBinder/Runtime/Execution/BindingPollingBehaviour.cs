using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    public abstract class BindingPollingBehaviour : MonoBehaviour
    {
        private bool schedulerRegistrationActive;

        protected virtual void OnEnable()
        {
            PrepareRuntime();
            schedulerRegistrationActive = true;
            BindingUpdateScheduler.Refresh(this);
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
            schedulerRegistrationActive = false;
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

        internal bool WantsScheduledTiming(BindingUpdateTiming timing)
        {
            return HasBindingsForTiming(timing) || HasAdditionalScheduledWork(timing);
        }

        protected void RefreshScheduledRegistration()
        {
            if (schedulerRegistrationActive)
            {
                BindingUpdateScheduler.Refresh(this);
            }
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

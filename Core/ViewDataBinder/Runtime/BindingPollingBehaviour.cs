using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    public abstract class BindingPollingBehaviour : MonoBehaviour
    {
        protected virtual void Awake()
        {
            ProcessBindingTiming(BindingUpdateTiming.Awake);
        }

        protected virtual void Start()
        {
            ProcessBindingTiming(BindingUpdateTiming.Start);
        }

        protected virtual void Update()
        {
            ProcessBindingTiming(BindingUpdateTiming.Update);
        }

        protected virtual void LateUpdate()
        {
            ProcessBindingTiming(BindingUpdateTiming.LateUpdate);
        }

        protected virtual void FixedUpdate()
        {
            ProcessBindingTiming(BindingUpdateTiming.FixedUpdate);
        }

        protected virtual void OnDisable()
        {
            ResetRuntimeState();
        }

        protected virtual void ResetRuntimeState()
        {
        }

        protected abstract void ProcessBindingTiming(BindingUpdateTiming timing);
    }
}

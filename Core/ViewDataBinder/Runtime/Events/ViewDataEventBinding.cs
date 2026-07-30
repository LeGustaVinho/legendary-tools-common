using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class ViewDataEventBinding
    {
        [SerializeField] private string id;
        [SerializeField] private string name = "Event Binding";
        [SerializeField] private bool enabled = true;
        [SerializeField] private BindingUpdateTiming updateTiming = BindingUpdateTiming.Update;
        [SerializeField] private bool triggerOnInitialize;
        [SerializeField] private BindingErrorPolicy errorPolicy = BindingErrorPolicy.ReportOnly;
        [SerializeField] private MissingEndpointPolicy sourceMissingPolicy = MissingEndpointPolicy.ReportError;
        [SerializeField, Min(0.01f)] private float missingEndpointRetryInterval = 0.1f;
        [SerializeField, Min(0.01f)] private float maximumMissingEndpointRetryInterval = 2f;
        [SerializeField] private List<BindingSource> sources =
            new List<BindingSource> { new BindingSource() };
        [SerializeField] private List<EventBindingCondition> conditions =
            new List<EventBindingCondition> { new EventBindingCondition() };

        public string Id
        {
            get => id;
            set => id = value;
        }

        public string Name
        {
            get => name;
            set => name = value ?? string.Empty;
        }

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public BindingUpdateTiming UpdateTiming
        {
            get => updateTiming;
            set => updateTiming = value;
        }

        public bool TriggerOnInitialize
        {
            get => triggerOnInitialize;
            set => triggerOnInitialize = value;
        }

        public BindingErrorPolicy ErrorPolicy
        {
            get => errorPolicy;
            set => errorPolicy = value;
        }

        public MissingEndpointPolicy SourceMissingPolicy
        {
            get => sourceMissingPolicy;
            set => sourceMissingPolicy = value;
        }

        public float MissingEndpointRetryInterval => Math.Max(0.01f, missingEndpointRetryInterval);

        public float MaximumMissingEndpointRetryInterval =>
            Math.Max(MissingEndpointRetryInterval, maximumMissingEndpointRetryInterval);

        public void SetMissingEndpointRetry(float initialInterval, float maximumInterval)
        {
            missingEndpointRetryInterval = Math.Max(0.01f, initialInterval);
            maximumMissingEndpointRetryInterval =
                Math.Max(missingEndpointRetryInterval, maximumInterval);
        }

        public IReadOnlyList<BindingSource> Sources => sources;

        public IReadOnlyList<EventBindingCondition> Conditions => conditions;

        public int AddSource(BindingSource source)
        {
            sources.Add(source ?? throw new ArgumentNullException(nameof(source)));
            return sources.Count - 1;
        }

        public bool RemoveSourceAt(int sourceIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= sources.Count)
            {
                return false;
            }

            sources.RemoveAt(sourceIndex);
            return true;
        }

        public void ClearSources()
        {
            sources.Clear();
        }

        public int AddCondition(EventBindingCondition condition)
        {
            conditions.Add(condition ?? throw new ArgumentNullException(nameof(condition)));
            return conditions.Count - 1;
        }

        public bool RemoveConditionAt(int conditionIndex)
        {
            if (conditionIndex < 0 || conditionIndex >= conditions.Count)
            {
                return false;
            }

            conditions[conditionIndex]?.ReleaseRuntimeResources();
            conditions.RemoveAt(conditionIndex);
            return true;
        }

        public void ClearConditions()
        {
            for (int i = 0; i < conditions.Count; i++)
            {
                conditions[i]?.ReleaseRuntimeResources();
            }

            conditions.Clear();
        }

        internal void EnsureId()
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("N");
            }
        }
    }
}

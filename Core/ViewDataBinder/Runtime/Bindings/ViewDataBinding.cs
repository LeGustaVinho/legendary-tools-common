using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class ViewDataBinding
    {
        [SerializeField] private string id;
        [SerializeField] private string name = "Binding";
        [SerializeField] private bool enabled = true;
        [SerializeField] private List<BindingSource> sources = new List<BindingSource> { new BindingSource() };
        [SerializeField] private BindingEndpoint target = new BindingEndpoint();
        [SerializeField] private BindingSyncDirection direction = BindingSyncDirection.SourceToTarget;
        [SerializeField] private BindingUpdateTiming updateTiming = BindingUpdateTiming.Update;
        [SerializeField] private BindingConflictResolution conflictResolution = BindingConflictResolution.SourceWins;
        [SerializeField] private BindingWritePolicy writePolicy = BindingWritePolicy.WhenValueChanges;
        [SerializeField] private BindingErrorPolicy errorPolicy = BindingErrorPolicy.ReportOnly;
        [SerializeField] private MissingEndpointPolicy sourceMissingPolicy = MissingEndpointPolicy.ReportError;
        [SerializeField] private MissingEndpointPolicy targetMissingPolicy = MissingEndpointPolicy.ReportError;
        [SerializeField, Min(0.01f)] private float missingEndpointRetryInterval = 0.1f;
        [SerializeField, Min(0.01f)] private float maximumMissingEndpointRetryInterval = 2f;
        [SerializeField] private bool alwaysEvaluateTransformation;
        [SerializeField] private BindingFormatterSettings formatter = new BindingFormatterSettings();
        [SerializeField] private BindingConverter converter;
        [SerializeField] private BindingNullHandlingMode nullHandling = BindingNullHandlingMode.PassThrough;
        [SerializeField] private BindingFallbackSettings fallback = new BindingFallbackSettings();

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

        public IReadOnlyList<BindingSource> Sources => sources;

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

        public BindingEndpoint Target
        {
            get => target;
            set => target = value ?? new BindingEndpoint();
        }

        public BindingSyncDirection Direction
        {
            get => direction;
            set => direction = value;
        }

        public BindingUpdateTiming UpdateTiming
        {
            get => updateTiming;
            set => updateTiming = value;
        }

        public BindingConflictResolution ConflictResolution
        {
            get => conflictResolution;
            set => conflictResolution = value;
        }

        public BindingWritePolicy WritePolicy
        {
            get => writePolicy;
            set => writePolicy = value;
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

        public MissingEndpointPolicy TargetMissingPolicy
        {
            get => targetMissingPolicy;
            set => targetMissingPolicy = value;
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

        public bool AlwaysEvaluateTransformation
        {
            get => alwaysEvaluateTransformation;
            set => alwaysEvaluateTransformation = value;
        }

        public BindingFormatterSettings Formatter => formatter;

        public BindingConverter Converter
        {
            get => converter;
            set => converter = value;
        }

        public BindingNullHandlingMode NullHandling
        {
            get => nullHandling;
            set => nullHandling = value;
        }

        public BindingFallbackSettings Fallback => fallback;

        internal void EnsureId()
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("N");
            }
        }
    }
}

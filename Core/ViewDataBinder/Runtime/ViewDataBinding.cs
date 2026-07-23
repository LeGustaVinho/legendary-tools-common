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

        public string Id => id;

        public string Name => name;

        public bool Enabled => enabled;

        public IReadOnlyList<BindingSource> Sources => sources;

        public BindingEndpoint Target => target;

        public BindingSyncDirection Direction => direction;

        public BindingUpdateTiming UpdateTiming => updateTiming;

        public BindingConflictResolution ConflictResolution => conflictResolution;

        public BindingWritePolicy WritePolicy => writePolicy;

        public BindingErrorPolicy ErrorPolicy => errorPolicy;

        public MissingEndpointPolicy SourceMissingPolicy => sourceMissingPolicy;

        public MissingEndpointPolicy TargetMissingPolicy => targetMissingPolicy;

        public float MissingEndpointRetryInterval => Math.Max(0.01f, missingEndpointRetryInterval);

        public float MaximumMissingEndpointRetryInterval =>
            Math.Max(MissingEndpointRetryInterval, maximumMissingEndpointRetryInterval);

        public bool AlwaysEvaluateTransformation => alwaysEvaluateTransformation;

        public BindingFormatterSettings Formatter => formatter;

        public BindingConverter Converter => converter;

        public BindingNullHandlingMode NullHandling => nullHandling;

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

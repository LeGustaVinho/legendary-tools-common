using System;
using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public sealed class BindingRuntimeState
    {
        private readonly List<BindingMemberMetadata> sourceMetadataBuffer =
            new List<BindingMemberMetadata>(1);
        private readonly List<object> sourceValueBuffer = new List<object>(1);
        private object[] lastSourceInputValues = Array.Empty<object>();
        private object[] formatterArguments = Array.Empty<object>();
        private object cachedSourceOutput;
        private bool hasCachedSourceOutput;
        private Dictionary<Type, CachedValue> fallbackValueCache;

        internal BindingExecutionPlan ExecutionPlan { get; } = new BindingExecutionPlan();

        public bool Initialized { get; set; }

        public object LastSourceValue { get; set; }

        public object LastTargetValue { get; set; }

        internal bool SourceInputsUnchanged { get; set; }

        public BindingSyncResult LastResult { get; set; }

        public bool HasResult { get; set; }

        public bool RuntimeDisabled { get; set; }

        public BindingSyncStatus LastLoggedStatus { get; set; }

        public string LastLoggedMessage { get; set; }

        public BindingEndpointRole MissingEndpointRole { get; private set; }

        public MissingEndpointPolicy MissingEndpointPolicy { get; private set; }

        public bool MissingEndpointActionApplied { get; private set; }

        internal float NextMissingEndpointRetryTime { get; set; }

        internal int MissingEndpointRetryAttempt { get; set; }

        internal BindingSyncResult CachedMissingEndpointResult { get; set; }

        internal bool HasCachedMissingEndpointResult { get; set; }

        internal List<BindingMemberMetadata> PrepareSourceMetadataBuffer(int capacity)
        {
            if (sourceMetadataBuffer.Capacity < capacity)
            {
                sourceMetadataBuffer.Capacity = capacity;
            }

            sourceMetadataBuffer.Clear();
            return sourceMetadataBuffer;
        }

        internal List<object> PrepareSourceValueBuffer(int capacity)
        {
            if (sourceValueBuffer.Capacity < capacity)
            {
                sourceValueBuffer.Capacity = capacity;
            }

            sourceValueBuffer.Clear();
            return sourceValueBuffer;
        }

        internal object[] PrepareFormatterArguments(int count)
        {
            if (formatterArguments.Length != count)
            {
                formatterArguments = new object[count];
            }

            return formatterArguments;
        }

        internal bool SourceInputsChanged(IReadOnlyList<object> values)
        {
            int count = values?.Count ?? 0;
            if (lastSourceInputValues.Length != count)
            {
                return true;
            }

            for (int i = 0; i < count; i++)
            {
                if (!BindingValueComparer.AreEqual(lastSourceInputValues[i], values[i]))
                {
                    return true;
                }
            }

            return false;
        }

        internal bool TryGetCachedSourceOutput(out object value)
        {
            value = cachedSourceOutput;
            return hasCachedSourceOutput;
        }

        internal void CacheSourceOutput(object value)
        {
            cachedSourceOutput = value;
            hasCachedSourceOutput = true;
        }

        internal void CaptureSourceInputs(IReadOnlyList<object> values)
        {
            int count = values?.Count ?? 0;
            if (lastSourceInputValues.Length != count)
            {
                lastSourceInputValues = new object[count];
            }

            for (int i = 0; i < count; i++)
            {
                lastSourceInputValues[i] = values[i];
            }
        }

        internal bool TryGetCachedFallback(Type type, BindingFallbackValue source, out object value)
        {
            if (type != null &&
                fallbackValueCache != null &&
                fallbackValueCache.TryGetValue(type, out CachedValue cached) &&
                ReferenceEquals(cached.Source, source))
            {
                value = cached.Value;
                return true;
            }

            value = null;
            return false;
        }

        internal void CacheFallback(Type type, BindingFallbackValue source, object value)
        {
            if (type != null)
            {
                if (fallbackValueCache == null)
                {
                    fallbackValueCache = new Dictionary<Type, CachedValue>();
                }

                fallbackValueCache[type] = new CachedValue(source, value);
            }
        }

        internal void MarkMissingEndpoint(
            BindingEndpointRole role,
            MissingEndpointPolicy policy)
        {
            if (MissingEndpointRole == role && MissingEndpointPolicy == policy)
            {
                return;
            }

            MissingEndpointRole = role;
            MissingEndpointPolicy = policy;
            MissingEndpointActionApplied = false;
            MissingEndpointRetryAttempt = 0;
            NextMissingEndpointRetryTime = 0f;
            HasCachedMissingEndpointResult = false;
        }

        internal void MarkMissingEndpointActionApplied()
        {
            MissingEndpointActionApplied = true;
        }

        internal void CacheMissingEndpointResult(BindingSyncResult result, float nextRetryTime)
        {
            CachedMissingEndpointResult = result;
            HasCachedMissingEndpointResult = true;
            NextMissingEndpointRetryTime = nextRetryTime;
            MissingEndpointRetryAttempt++;
        }

        internal void ClearMissingEndpoint()
        {
            MissingEndpointRole = BindingEndpointRole.None;
            MissingEndpointPolicy = MissingEndpointPolicy.ReportError;
            MissingEndpointActionApplied = false;
            MissingEndpointRetryAttempt = 0;
            NextMissingEndpointRetryTime = 0f;
            CachedMissingEndpointResult = default;
            HasCachedMissingEndpointResult = false;
        }

        internal void InvalidateExecutionPlan()
        {
            ExecutionPlan.Invalidate();
            fallbackValueCache?.Clear();
        }

        internal void ResetSynchronizationValues()
        {
            ExecutionPlan.ResetRuntimeState();
            Initialized = false;
            LastSourceValue = null;
            LastTargetValue = null;
            SourceInputsUnchanged = false;
            cachedSourceOutput = null;
            hasCachedSourceOutput = false;
            sourceMetadataBuffer.Clear();
            sourceValueBuffer.Clear();
            Array.Clear(lastSourceInputValues, 0, lastSourceInputValues.Length);
            Array.Clear(formatterArguments, 0, formatterArguments.Length);
        }

        public void ResetValues()
        {
            ResetSynchronizationValues();
            HasResult = false;
            RuntimeDisabled = false;
            LastLoggedStatus = BindingSyncStatus.Success;
            LastLoggedMessage = null;
            ClearMissingEndpoint();
        }

        public void ReleaseResources()
        {
            ResetValues();
            InvalidateExecutionPlan();
            sourceMetadataBuffer.Clear();
            sourceMetadataBuffer.TrimExcess();
            sourceValueBuffer.Clear();
            sourceValueBuffer.TrimExcess();
            lastSourceInputValues = Array.Empty<object>();
            formatterArguments = Array.Empty<object>();
            fallbackValueCache?.Clear();
            fallbackValueCache = null;
        }

        private readonly struct CachedValue
        {
            public CachedValue(BindingFallbackValue source, object value)
            {
                Source = source;
                Value = value;
            }

            public BindingFallbackValue Source { get; }

            public object Value { get; }
        }
    }
}

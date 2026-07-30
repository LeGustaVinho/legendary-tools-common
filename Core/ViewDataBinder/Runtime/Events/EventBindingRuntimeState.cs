using System;

namespace LegendaryTools.ViewBinding
{
    public sealed class EventBindingRuntimeState
    {
        public bool Initialized { get; set; }

        public bool MetadataInitialized { get; set; }

        public object[] LastValues { get; private set; } = Array.Empty<object>();

        public object[] CurrentValues { get; private set; } = Array.Empty<object>();

        public BindingMemberMetadata[] SourceMetadata { get; private set; } =
            Array.Empty<BindingMemberMetadata>();

        public bool[] ChangedSources { get; private set; } = Array.Empty<bool>();

        private object[] sourceIdentities = Array.Empty<object>();
        private Type[] sourceResolvedTypes = Array.Empty<Type>();
        private bool[] sourceStaticFlags = Array.Empty<bool>();

        public BindingSyncResult LastResult { get; set; }

        public bool HasResult { get; set; }

        public bool RuntimeDisabled { get; set; }

        public bool HasRunningTasks { get; set; }

        public BindingSyncStatus LastLoggedStatus { get; set; }

        public string LastLoggedMessage { get; set; }

        public bool SourceEndpointMissing { get; private set; }

        public MissingEndpointPolicy MissingEndpointPolicy { get; private set; }

        internal float NextMissingEndpointRetryTime { get; set; }

        internal int MissingEndpointRetryAttempt { get; set; }

        internal BindingSyncResult CachedMissingEndpointResult { get; set; }

        internal bool HasCachedMissingEndpointResult { get; set; }

        public void EnsureSourceCount(int sourceCount)
        {
            if (LastValues.Length == sourceCount)
            {
                return;
            }

            LastValues = new object[sourceCount];
            CurrentValues = new object[sourceCount];
            SourceMetadata = new BindingMemberMetadata[sourceCount];
            ChangedSources = new bool[sourceCount];
            sourceIdentities = new object[sourceCount];
            sourceResolvedTypes = new Type[sourceCount];
            sourceStaticFlags = new bool[sourceCount];
            Initialized = false;
            MetadataInitialized = false;
        }


        internal bool MatchesResolution(ViewDataEventBinding binding)
        {
            int sourceCount = binding?.Sources?.Count ?? 0;
            if (sourceIdentities.Length != sourceCount)
            {
                return false;
            }

            for (int i = 0; i < sourceCount; i++)
            {
                if (!BindingExecutionPlan.MatchesEndpointResolution(
                        binding.Sources[i]?.Endpoint,
                        sourceIdentities[i],
                        sourceResolvedTypes[i],
                        sourceStaticFlags[i]))
                {
                    return false;
                }
            }

            return true;
        }

        internal bool TryCaptureResolution(
            ViewDataEventBinding binding,
            out string error)
        {
            int sourceCount = binding?.Sources?.Count ?? 0;
            EnsureSourceCount(sourceCount);
            for (int i = 0; i < sourceCount; i++)
            {
                if (!BindingExecutionPlan.TryResolveIdentity(
                        binding.Sources[i]?.Endpoint,
                        out sourceIdentities[i],
                        out sourceResolvedTypes[i],
                        out sourceStaticFlags[i],
                        out error))
                {
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        internal void MarkSourceEndpointMissing(MissingEndpointPolicy policy)
        {
            if (SourceEndpointMissing && MissingEndpointPolicy == policy)
            {
                return;
            }

            SourceEndpointMissing = true;
            MissingEndpointPolicy = policy;
            MissingEndpointRetryAttempt = 0;
            NextMissingEndpointRetryTime = 0f;
            HasCachedMissingEndpointResult = false;
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
            SourceEndpointMissing = false;
            MissingEndpointPolicy = MissingEndpointPolicy.ReportError;
            MissingEndpointRetryAttempt = 0;
            NextMissingEndpointRetryTime = 0f;
            CachedMissingEndpointResult = default;
            HasCachedMissingEndpointResult = false;
        }

        internal void ResetObservation(bool invalidateMetadata = false)
        {
            Initialized = false;
            Array.Clear(LastValues, 0, LastValues.Length);
            Array.Clear(CurrentValues, 0, CurrentValues.Length);
            Array.Clear(ChangedSources, 0, ChangedSources.Length);
            if (invalidateMetadata)
            {
                Array.Clear(SourceMetadata, 0, SourceMetadata.Length);
                Array.Clear(sourceIdentities, 0, sourceIdentities.Length);
                Array.Clear(sourceResolvedTypes, 0, sourceResolvedTypes.Length);
                Array.Clear(sourceStaticFlags, 0, sourceStaticFlags.Length);
                MetadataInitialized = false;
            }
        }

        public void Reset()
        {
            ResetObservation();
            HasResult = false;
            RuntimeDisabled = false;
            HasRunningTasks = false;
            LastLoggedStatus = BindingSyncStatus.Success;
            LastLoggedMessage = null;
            ClearMissingEndpoint();
        }

        public void InvalidatePlan()
        {
            ResetObservation(true);
            ClearMissingEndpoint();
        }

        public void ReleaseResources()
        {
            Reset();
            LastValues = Array.Empty<object>();
            CurrentValues = Array.Empty<object>();
            SourceMetadata = Array.Empty<BindingMemberMetadata>();
            ChangedSources = Array.Empty<bool>();
            sourceIdentities = Array.Empty<object>();
            sourceResolvedTypes = Array.Empty<Type>();
            sourceStaticFlags = Array.Empty<bool>();
            MetadataInitialized = false;
        }
    }
}

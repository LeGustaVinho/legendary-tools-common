using System;

namespace LegendaryTools.ViewBinding
{
    public sealed class EventBindingRuntimeState
    {
        public bool Initialized { get; set; }

        public object[] LastValues { get; private set; } = Array.Empty<object>();

        public object[] CurrentValues { get; private set; } = Array.Empty<object>();

        public BindingMemberMetadata[] SourceMetadata { get; private set; } =
            Array.Empty<BindingMemberMetadata>();

        public bool[] ChangedSources { get; private set; } = Array.Empty<bool>();

        public BindingSyncResult LastResult { get; set; }

        public bool HasResult { get; set; }

        public bool RuntimeDisabled { get; set; }

        public bool HasRunningTasks { get; set; }

        public BindingSyncStatus LastLoggedStatus { get; set; }

        public string LastLoggedMessage { get; set; }

        public bool SourceEndpointMissing { get; private set; }

        public MissingEndpointPolicy MissingEndpointPolicy { get; private set; }

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
            Initialized = false;
        }

        internal void MarkSourceEndpointMissing(MissingEndpointPolicy policy)
        {
            SourceEndpointMissing = true;
            MissingEndpointPolicy = policy;
        }

        internal void ClearMissingEndpoint()
        {
            SourceEndpointMissing = false;
            MissingEndpointPolicy = MissingEndpointPolicy.ReportError;
        }

        internal void ResetObservation()
        {
            Initialized = false;
            Array.Clear(LastValues, 0, LastValues.Length);
            Array.Clear(CurrentValues, 0, CurrentValues.Length);
            Array.Clear(SourceMetadata, 0, SourceMetadata.Length);
            Array.Clear(ChangedSources, 0, ChangedSources.Length);
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
    }
}

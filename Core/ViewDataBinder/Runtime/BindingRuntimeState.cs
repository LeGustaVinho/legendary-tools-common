using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public sealed class BindingRuntimeState
    {
        private readonly List<BindingMemberMetadata> sourceMetadataBuffer =
            new List<BindingMemberMetadata>(1);
        private readonly List<object> sourceValueBuffer = new List<object>(1);

        public bool Initialized { get; set; }

        public object LastSourceValue { get; set; }

        public object LastTargetValue { get; set; }

        public BindingSyncResult LastResult { get; set; }

        public bool HasResult { get; set; }

        public bool RuntimeDisabled { get; set; }

        public BindingSyncStatus LastLoggedStatus { get; set; }

        public string LastLoggedMessage { get; set; }

        public BindingEndpointRole MissingEndpointRole { get; private set; }

        public MissingEndpointPolicy MissingEndpointPolicy { get; private set; }

        public bool MissingEndpointActionApplied { get; private set; }

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
        }

        internal void MarkMissingEndpointActionApplied()
        {
            MissingEndpointActionApplied = true;
        }

        internal void ClearMissingEndpoint()
        {
            MissingEndpointRole = BindingEndpointRole.None;
            MissingEndpointPolicy = MissingEndpointPolicy.ReportError;
            MissingEndpointActionApplied = false;
        }

        internal void ResetSynchronizationValues()
        {
            Initialized = false;
            LastSourceValue = null;
            LastTargetValue = null;
            sourceMetadataBuffer.Clear();
            sourceValueBuffer.Clear();
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
    }
}

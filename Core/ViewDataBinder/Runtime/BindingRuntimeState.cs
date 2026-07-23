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

        public void ResetValues()
        {
            Initialized = false;
            LastSourceValue = null;
            LastTargetValue = null;
            sourceMetadataBuffer.Clear();
            sourceValueBuffer.Clear();
            HasResult = false;
            RuntimeDisabled = false;
            LastLoggedStatus = BindingSyncStatus.Success;
            LastLoggedMessage = null;
        }
    }
}

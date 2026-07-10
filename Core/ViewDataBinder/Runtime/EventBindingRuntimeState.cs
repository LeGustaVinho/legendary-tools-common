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

        public void Reset()
        {
            Initialized = false;
            Array.Clear(LastValues, 0, LastValues.Length);
            Array.Clear(CurrentValues, 0, CurrentValues.Length);
            Array.Clear(SourceMetadata, 0, SourceMetadata.Length);
            Array.Clear(ChangedSources, 0, ChangedSources.Length);
        }
    }
}

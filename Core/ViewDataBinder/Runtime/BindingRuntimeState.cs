namespace LegendaryTools.ViewBinding
{
    public sealed class BindingRuntimeState
    {
        public bool Initialized { get; set; }

        public object LastSourceValue { get; set; }

        public object LastTargetValue { get; set; }

        public BindingSyncResult LastResult { get; set; }

        public void ResetValues()
        {
            Initialized = false;
            LastSourceValue = null;
            LastTargetValue = null;
        }
    }
}

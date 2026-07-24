namespace LegendaryTools.ViewBinding
{
    internal sealed class BindingExecutionEntry
    {
        public BindingExecutionEntry(
            ViewDataBinding binding,
            string stateKey,
            ViewDataBindingProfileReference profileReference,
            BindingRuntimeState state)
        {
            Binding = binding;
            StateKey = stateKey;
            ProfileReference = profileReference;
            State = state;
        }

        public ViewDataBinding Binding { get; }

        public string StateKey { get; }

        public ViewDataBindingProfileReference ProfileReference { get; }

        public BindingRuntimeState State { get; }
    }

    internal sealed class EventBindingExecutionEntry
    {
        public EventBindingExecutionEntry(
            int index,
            ViewDataEventBinding binding,
            EventBindingRuntimeState state)
        {
            Index = index;
            Binding = binding;
            State = state;
        }

        public int Index { get; }

        public ViewDataEventBinding Binding { get; }

        public EventBindingRuntimeState State { get; }
    }
}

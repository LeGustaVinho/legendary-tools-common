namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Read/write interface for variables.
    /// </summary>
    public interface IVariable<T> : IReadOnlyVariable<T>
    {
        /// <summary>Set the runtime value. Triggers change event if different.</summary>
        void SetValue(T value);

        /// <summary>Set without invoking change event.</summary>
        void SetValueSilent(T value);

        /// <summary>Reset runtime value to the configured initial value.</summary>
        void ResetToInitial();
    }
}
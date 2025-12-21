namespace LegendaryTools.AttributeSystem
{
    /// <summary>
    /// Results that can be returned by calls that manipulate Attribute capacity.
    /// </summary>
    public enum AttributeUsageStatus
    {
        /// <summary>
        /// The operation completed successfully (no warnings).
        /// </summary>
        Success,

        /// <summary>
        /// The attribute cannot hold a capacity usage (either capacity is disabled or type does not support it).
        /// </summary>
        ErrorNoCapacity,

        /// <summary>
        /// The operation failed because a negative value was provided.
        /// </summary>
        ErrorNegativeValue,

        /// <summary>
        /// The requested addition exceeded the maximum allowed capacity, so the value was clamped down.
        /// </summary>
        WarningClampedToMax,

        /// <summary>
        /// The final new capacity would have fallen below the minimum allowed, so the operation was rejected.
        /// </summary>
        ErrorBelowMinimum,

        /// <summary>
        /// The requested removal caused the capacity to fall below the minimum value, so it was clamped up.
        /// </summary>
        WarningClampedToMinimum
    }
}
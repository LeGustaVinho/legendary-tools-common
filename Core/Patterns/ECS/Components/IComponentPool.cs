namespace LegendaryTools.Common.Core.Patterns.ECS.Components
{
    /// <summary>
    /// Non-generic interface for per-component storage pools.
    /// </summary>
    internal interface IComponentPool
    {
        /// <summary>
        /// Ensures the pool can store data up to the given entity index capacity.
        /// </summary>
        /// <param name="requiredCapacity">Required capacity (entity array length).</param>
        void EnsureCapacity(int requiredCapacity);
    }
}

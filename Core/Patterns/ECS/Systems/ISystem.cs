using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Systems
{
    /// <summary>
    /// ECS system lifecycle contract.
    /// </summary>
    public interface ISystem
    {
        /// <summary>
        /// Called once when the system is added to a scheduler.
        /// </summary>
        /// <param name="world">Target world.</param>
        void OnCreate(World world);

        /// <summary>
        /// Called every tick during the simulation phase(s).
        /// </summary>
        /// <param name="world">Target world.</param>
        /// <param name="tick">Current tick.</param>
        void OnUpdate(World world, int tick);

        /// <summary>
        /// Called once when the scheduler is destroyed.
        /// </summary>
        /// <param name="world">Target world.</param>
        void OnDestroy(World world);
    }
}
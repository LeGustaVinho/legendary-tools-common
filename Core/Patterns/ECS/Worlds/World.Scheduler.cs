using LegendaryTools.Common.Core.Patterns.ECS.Systems;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Gets the world's default scheduler instance (optional convenience).
        /// </summary>
        public Scheduler Scheduler { get; private set; }

        /// <summary>
        /// Creates a default scheduler bound to this world.
        /// </summary>
        /// <returns>Scheduler instance.</returns>
        public Scheduler CreateScheduler()
        {
            if (Scheduler == null) Scheduler = new Scheduler(this);

            return Scheduler;
        }
    }
}
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Systems
{
    /// <summary>
    /// Minimal base class to reduce boilerplate in gameplay systems.
    /// </summary>
    public abstract class SystemBase : ISystem
    {
        public virtual void OnCreate(World world)
        {
        }

        public virtual void OnDestroy(World world)
        {
        }

        public void OnUpdate(World world, int tick)
        {
            Update(world, tick);
        }

        /// <summary>
        /// Override this method instead of OnUpdate.
        /// </summary>
        protected abstract void Update(World world, int tick);
    }
}
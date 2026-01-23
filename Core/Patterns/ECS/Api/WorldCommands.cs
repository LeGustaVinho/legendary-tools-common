using System;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Api
{
    /// <summary>
    /// A small facade that makes gameplay code easier:
    /// - Outside update: applies immediately.
    /// - During update: routes to ECB.
    /// Determinism rules are preserved.
    /// </summary>
    public readonly struct WorldCommands
    {
        private readonly World _world;

        public WorldCommands(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public Entity CreateEntity(int sortKey = 0)
        {
            if (_world.State.IsUpdating)
                return _world.ECB.CreateEntity(sortKey);

            // Outside update, immediate structural change is allowed.
            return _world.CreateEntity();
        }

        public void DestroyEntity(Entity entity, int sortKey = 0)
        {
            if (_world.State.IsUpdating)
            {
                _world.ECB.DestroyEntity(entity, sortKey);
                return;
            }

            _world.DestroyEntity(entity);
        }

        public void Add<T>(Entity entity, int sortKey = 0) where T : struct
        {
            Add(entity, default(T), sortKey);
        }

        public void Add<T>(Entity entity, in T value, int sortKey = 0) where T : struct
        {
            if (_world.State.IsUpdating)
            {
                _world.ECB.Add(entity, value, sortKey);
                return;
            }

            _world.Add(entity, value);
        }

        public void Remove<T>(Entity entity, int sortKey = 0) where T : struct
        {
            if (_world.State.IsUpdating)
            {
                _world.ECB.Remove<T>(entity, sortKey);
                return;
            }

            _world.Remove<T>(entity);
        }
    }
}
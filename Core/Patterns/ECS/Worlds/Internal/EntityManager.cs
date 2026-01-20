using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    /// <summary>
    /// Manages entity lifetime: (Index, Version), alive flags, and free-list.
    /// Storage placement/removal is handled elsewhere.
    /// </summary>
    internal sealed class EntityManager
    {
        private readonly WorldState _state;

        public EntityManager(WorldState state)
        {
            _state = state;
        }

        public Entity CreateEntity()
        {
            int index;

            if (_state.TryPopFreeIndex(out int free))
            {
                index = free;
            }
            else
            {
                index = _state.NextIndex++;
                _state.EnsureEntityCapacity(index + 1);
            }

            _state.Alive[index] = true;

            return new Entity(index, _state.Versions[index]);
        }

        public bool IsAlive(Entity entity)
        {
            int index = entity.Index;

            if ((uint)index >= (uint)_state.Alive.Length)
            {
                return false;
            }

            if (!_state.Alive[index])
            {
                return false;
            }

            return _state.Versions[index] == entity.Version;
        }

        public void FinalizeDestroy(Entity entity)
        {
            int index = entity.Index;

            _state.Locations[index] = EntityLocation.Invalid;

            _state.Alive[index] = false;

            unchecked
            {
                _state.Versions[index]++;
            }

            _state.PushFreeIndex(index);
        }
    }
}

using System;

using LegendaryTools.Common.Core.Patterns.ECS.Entities;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    public sealed partial class World
    {
        /// <summary>
        /// Creates a new entity in the empty archetype.
        /// Forbidden during tick update; use <see cref="ECB"/> instead.
        /// </summary>
        /// <returns>The created entity.</returns>
        public Entity CreateEntity()
        {
            if (State.IsUpdating)
            {
                throw new InvalidOperationException("Structural changes are not allowed during update. Use ECB.CreateEntity().");
            }

            return InternalCreateEntity();
        }

        /// <summary>
        /// Destroys an entity. Safe to call multiple times; only the first call has effect.
        /// Forbidden during tick update; use <see cref="ECB"/> instead.
        /// </summary>
        /// <param name="e">The entity to destroy.</param>
        public void DestroyEntity(Entity e)
        {
            if (State.IsUpdating)
            {
                throw new InvalidOperationException("Structural changes are not allowed during update. Use ECB.DestroyEntity(entity).");
            }

            Structural.AssertNotIterating();
            InternalDestroyEntity(e);
        }

        /// <summary>
        /// Checks whether the entity is alive and matches the current version.
        /// </summary>
        /// <param name="e">Entity to check.</param>
        /// <returns>True if alive and not stale.</returns>
        public bool IsAlive(Entity e) => Entities.IsAlive(e);

        /// <summary>
        /// Gets the current count of alive entities.
        /// Note: this is an O(N) scan. Intended for debugging, HUD, and tooling (not hot path).
        /// </summary>
        /// <returns>Alive entity count.</returns>
        public int GetAliveEntityCount()
        {
            // Only scan up to NextIndex (indices beyond that were never allocated).
            int max = State.NextIndex;
            if (max <= 0)
            {
                return 0;
            }

            bool[] alive = State.Alive;
            if (max > alive.Length)
            {
                max = alive.Length;
            }

            int count = 0;
            for (int i = 0; i < max; i++)
            {
                if (alive[i])
                {
                    count++;
                }
            }

            return count;
        }
    }
}

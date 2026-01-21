using System;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    /// <summary>
    /// Lightweight query result wrapper that keeps the world in an "iteration" state until disposed.
    /// Prefer this for high-performance loops where you want to iterate chunks/columns using spans and plain for loops
    /// (no per-entity callbacks).
    /// </summary>
    public readonly ref struct WorldQueryResult
    {
        private readonly World _world;
        private readonly Archetype[] _buffer;
        private readonly int _count;

        internal WorldQueryResult(World world, Archetype[] buffer, int count)
        {
            _world = world;
            _buffer = buffer;
            _count = count;
        }

        /// <summary>
        /// Matching archetypes for the query (stable order), as a span over the cached array.
        /// </summary>
        public ReadOnlySpan<Archetype> Archetypes
        {
            get
            {
                if (_buffer == null || _count <= 0) return ReadOnlySpan<Archetype>.Empty;
                return new ReadOnlySpan<Archetype>(_buffer, 0, _count);
            }
        }

        /// <summary>
        /// Ends the iteration scope. Use with "using var".
        /// </summary>
        public void Dispose()
        {
            _world.ExitIteration();
        }
    }
}
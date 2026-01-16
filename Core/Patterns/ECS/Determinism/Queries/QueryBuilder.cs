#nullable enable

using System;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Builder for deterministic <see cref="Query"/> instances.
    /// </summary>
    /// <remarks>
    /// Queries should be created during boot/warmup (after registering components).
    /// After warmup, iteration via World.ForEachChunk performs zero allocations.
    /// </remarks>
    public static class QueryBuilder
    {
        /// <summary>
        /// Creates an empty query for the given world (masks sized to the current component registry count).
        /// </summary>
        public static Query Create(World world)
        {
            if (world is null)
                throw new ArgumentNullException(nameof(world));

            return new Query(world.Components.Count);
        }

        /// <summary>
        /// Adds a required component to the query (All).
        /// </summary>
        public static Query All<T>(this Query q, World world) where T : struct
        {
            if (q is null) throw new ArgumentNullException(nameof(q));
            if (world is null) throw new ArgumentNullException(nameof(world));

            q.AddAll(world.Components.GetId(typeof(T)));
            return q;
        }

        /// <summary>
        /// Adds a forbidden component to the query (None).
        /// </summary>
        public static Query None<T>(this Query q, World world) where T : struct
        {
            if (q is null) throw new ArgumentNullException(nameof(q));
            if (world is null) throw new ArgumentNullException(nameof(world));

            q.AddNone(world.Components.GetId(typeof(T)));
            return q;
        }

        /// <summary>
        /// Adds an optional component to the query (Any).
        /// </summary>
        public static Query Any<T>(this Query q, World world) where T : struct
        {
            if (q is null) throw new ArgumentNullException(nameof(q));
            if (world is null) throw new ArgumentNullException(nameof(world));

            q.AddAny(world.Components.GetId(typeof(T)));
            return q;
        }
    }
}
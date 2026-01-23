using System;
using System.Text;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal;

namespace LegendaryTools.Common.Core.Patterns.ECS.Observability
{
    /// <summary>
    /// Pull-based world inspection utilities.
    /// Safe for debug, tooling, and extensions.
    /// </summary>
    public static class EcsWorldInspector
    {
        /// <summary>
        /// Returns a lightweight world snapshot (no allocations besides small locals).
        /// </summary>
        public static EcsWorldStats GetStats(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            WorldState s = world.State;

            int aliveCount = 0;
            int max = s.NextIndex;
            if (max > s.Alive.Length) max = s.Alive.Length;

            for (int i = 0; i < max; i++)
            {
                if (s.Alive[i]) aliveCount++;
            }

            int archetypeCount = 0;
            int chunkCount = 0;

            ArchetypeEnumerable e = world.Storage.EnumerateArchetypesStable();
            ArchetypeEnumerable.Enumerator it = e.GetEnumerator();
            while (it.MoveNext())
            {
                Archetype a = it.Current;
                if (a == null) continue;

                archetypeCount++;
                chunkCount += a.ChunkCount;
            }

            return new EcsWorldStats(
                s.CurrentTick,
                s.SimulationHz,
                s.NextIndex,
                aliveCount,
                s.FreeCount,
                s.StructuralVersion,
                s.ArchetypeVersion,
                archetypeCount,
                chunkCount,
                world.GetComponentManifest());
        }

        /// <summary>
        /// Returns debug info for a single entity index/version handle.
        /// </summary>
        public static EcsEntityDebugInfo GetEntityInfo(World world, Entity entity)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            WorldState s = world.State;

            int currentVersion = (uint)entity.Index < (uint)s.Versions.Length ? s.Versions[entity.Index] : -1;
            bool isAlive = world.IsAlive(entity);

            EntityLocation loc = EntityLocation.Invalid;
            if ((uint)entity.Index < (uint)s.Locations.Length) loc = s.Locations[entity.Index];

            return new EcsEntityDebugInfo(entity, isAlive, currentVersion, loc);
        }

        /// <summary>
        /// Builds an archetype stats snapshot (allocates for the signature copy).
        /// Intended for debug UI.
        /// </summary>
        public static EcsArchetypeStats GetArchetypeStats(World world, Archetype archetype)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (archetype == null) throw new ArgumentNullException(nameof(archetype));

            int chunkCapacity = world.State.ChunkCapacity;

            int entityCount = 0;
            Chunk[] chunks = archetype.ChunksBuffer;
            int chunkCount = archetype.ChunkCount;

            for (int i = 0; i < chunkCount; i++)
            {
                entityCount += chunks[i].Count;
            }

            int totalCapacity = chunkCount * chunkCapacity;

            // Copy for stable external use (debug tools).
            int[] typeIds = archetype.Signature.TypeIds;
            int[] typeIdsCopy = new int[typeIds.Length];
            Array.Copy(typeIds, typeIdsCopy, typeIds.Length);

            return new EcsArchetypeStats(
                archetype.ArchetypeId,
                typeIdsCopy,
                chunkCount,
                entityCount,
                chunkCapacity,
                totalCapacity);
        }

        /// <summary>
        /// Dumps a readable summary of the world. Allocates a StringBuilder.
        /// </summary>
        public static string DumpWorld(World world, bool includeArchetypes = true, bool includeChunks = false)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            EcsWorldStats stats = GetStats(world);

            StringBuilder sb = new(4096);
            sb.AppendLine(stats.ToString());

            if (!includeArchetypes)
                return sb.ToString();

            sb.AppendLine("Archetypes:");

            ArchetypeEnumerable e = world.Storage.EnumerateArchetypesStable();
            ArchetypeEnumerable.Enumerator it = e.GetEnumerator();

            while (it.MoveNext())
            {
                Archetype a = it.Current;
                if (a == null) continue;

                EcsArchetypeStats aStats = GetArchetypeStats(world, a);

                sb.Append("  - ").Append(aStats.ArchetypeId)
                    .Append(" Types=").Append(aStats.SignatureTypeIds.Length)
                    .Append(" Chunks=").Append(aStats.ChunkCount)
                    .Append(" Entities=").Append(aStats.EntityCount)
                    .Append(" Cap=").Append(aStats.TotalCapacity)
                    .Append(" Util=").Append(aStats.Utilization01.ToString("0.000"))
                    .AppendLine();

                if (!includeChunks) continue;

                Chunk[] chunks = a.ChunksBuffer;
                for (int i = 0; i < a.ChunkCount; i++)
                {
                    Chunk c = chunks[i];
                    sb.Append("      chunk#").Append(c.ChunkId)
                        .Append(" count=").Append(c.Count)
                        .Append(" cap=").Append(c.Capacity)
                        .AppendLine();
                }
            }

            return sb.ToString();
        }
    }
}
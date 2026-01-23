using System;
using System.Text;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal;

namespace LegendaryTools.Common.Core.Patterns.ECS.Observability
{
    /// <summary>
    /// Validates critical ECS invariants to catch corruption early.
    /// Intended for debug/tests.
    /// </summary>
    public static class EcsInvariantValidator
    {
        /// <summary>
        /// Validates entity locations and chunk contents consistency.
        /// Returns true if OK; otherwise false and fills error message.
        /// </summary>
        public static bool Validate(World world, out string error)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            WorldState s = world.State;

            int max = s.NextIndex;
            if (max > s.Alive.Length) max = s.Alive.Length;
            if (max > s.Locations.Length) max = s.Locations.Length;

            for (int i = 0; i < max; i++)
            {
                bool alive = s.Alive[i];
                EntityLocation loc = s.Locations[i];

                if (!alive)
                {
                    // If not alive, location should be invalid.
                    if (loc.IsValid)
                    {
                        error =
                            $"Invariant failed: entity index {i} is not alive but has a valid location: {FormatLoc(loc)}";
                        return false;
                    }

                    continue;
                }

                // Alive entities must have valid location.
                if (!loc.IsValid)
                {
                    error = $"Invariant failed: entity index {i} is alive but has invalid location.";
                    return false;
                }

                // Archetype must exist.
                Archetype archetype;
                try
                {
                    archetype = world.Storage.GetArchetypeById(loc.ArchetypeId);
                }
                catch (Exception ex)
                {
                    error =
                        $"Invariant failed: entity index {i} refers to missing archetype {loc.ArchetypeId}. Exception: {ex.Message}";
                    return false;
                }

                // Chunk must exist.
                Chunk chunk;
                try
                {
                    chunk = archetype.GetChunkById(loc.ChunkId);
                }
                catch (Exception ex)
                {
                    error =
                        $"Invariant failed: entity index {i} refers to invalid chunk {loc.ChunkId} in archetype {loc.ArchetypeId}. Exception: {ex.Message}";
                    return false;
                }

                // Row must be in range.
                if ((uint)loc.Row >= (uint)chunk.Count)
                {
                    error =
                        $"Invariant failed: entity index {i} row {loc.Row} is out of range (chunk count {chunk.Count}).";
                    return false;
                }

                // Chunk entity at row must match index/version.
                Entity stored = chunk.Entities[loc.Row];
                int version = (uint)i < (uint)s.Versions.Length ? s.Versions[i] : -1;

                if (stored.Index != i || stored.Version != version)
                {
                    error =
                        $"Invariant failed: location mismatch for entity index {i}.\n" +
                        $"Expected in chunk: Entity(Index={i}, Version={version})\n" +
                        $"Found: {stored}\n" +
                        $"Loc: {FormatLoc(loc)}";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Same validation, but returns a readable report string (allocating).
        /// </summary>
        public static string ValidateOrReport(World world)
        {
            if (Validate(world, out string error))
                return "ECS invariants OK.";

            StringBuilder sb = new(512);
            sb.AppendLine("ECS invariants FAILED:");
            sb.AppendLine(error);
            sb.AppendLine();
            sb.AppendLine(EcsWorldInspector.DumpWorld(world, true, false));
            return sb.ToString();
        }

        private static string FormatLoc(EntityLocation loc)
        {
            return $"(Archetype={loc.ArchetypeId}, Chunk={loc.ChunkId}, Row={loc.Row})";
        }
    }
}
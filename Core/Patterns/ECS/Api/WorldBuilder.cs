using System;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds;

namespace LegendaryTools.Common.Core.Patterns.ECS.Api
{
    /// <summary>
    /// Simple fluent builder to bootstrap a World in a predictable way.
    /// </summary>
    public sealed class WorldBuilder
    {
        private WorldConfig _config;

        public WorldBuilder()
        {
            _config = WorldConfig.Default;
        }

        public WorldBuilder WithInitialCapacity(int value)
        {
            _config = new WorldConfig(
                value,
                _config.ChunkCapacity,
                _config.RemovalPolicy,
                _config.AllocationPolicy,
                _config.SimulationHz,
                _config.Deterministic);
            return this;
        }

        public WorldBuilder WithChunkCapacity(int value)
        {
            _config = new WorldConfig(
                _config.InitialCapacity,
                value,
                _config.RemovalPolicy,
                _config.AllocationPolicy,
                _config.SimulationHz,
                _config.Deterministic);
            return this;
        }

        public WorldBuilder WithPolicies(
            StorageRemovalPolicy removalPolicy,
            ChunkAllocationPolicy allocationPolicy)
        {
            _config = new WorldConfig(
                _config.InitialCapacity,
                _config.ChunkCapacity,
                removalPolicy,
                allocationPolicy,
                _config.SimulationHz,
                _config.Deterministic);
            return this;
        }

        public WorldBuilder WithSimulationHz(int value)
        {
            _config = new WorldConfig(
                _config.InitialCapacity,
                _config.ChunkCapacity,
                _config.RemovalPolicy,
                _config.AllocationPolicy,
                value,
                _config.Deterministic);
            return this;
        }

        public WorldBuilder Deterministic(bool enabled = true)
        {
            _config = new WorldConfig(
                _config.InitialCapacity,
                _config.ChunkCapacity,
                _config.RemovalPolicy,
                _config.AllocationPolicy,
                _config.SimulationHz,
                enabled);
            return this;
        }

        /// <summary>
        /// Builds a world using the current config.
        /// </summary>
        public World Build()
        {
            return new World(
                _config.InitialCapacity,
                _config.ChunkCapacity,
                _config.RemovalPolicy,
                _config.AllocationPolicy,
                _config.SimulationHz,
                _config.Deterministic);
        }

        /// <summary>
        /// Convenience build + register components in one place.
        /// The world must be used only after registration in deterministic mode.
        /// </summary>
        public World BuildAndRegister(params Action<World>[] registrations)
        {
            if (registrations == null) throw new ArgumentNullException(nameof(registrations));

            World world = Build();
            for (int i = 0; i < registrations.Length; i++)
            {
                registrations[i]?.Invoke(world);
            }

            return world;
        }
    }
}
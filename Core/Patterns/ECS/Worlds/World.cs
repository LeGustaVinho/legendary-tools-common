using LegendaryTools.Common.Core.Patterns.ECS.Storage;
using LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds
{
    /// <summary>
    /// ECS World facade. Owns state and delegates to focused internal services.
    /// </summary>
    public sealed partial class World
    {
        internal readonly WorldState State;
        internal readonly EntityManager Entities;
        internal readonly StorageService Storage;
        internal readonly StructuralChanges Structural;

        /// <summary>
        /// Creates a deterministic ECS world.
        /// </summary>
        /// <param name="initialCapacity">Initial entity capacity.</param>
        /// <param name="chunkCapacity">Number of entities per chunk.</param>
        /// <param name="removalPolicy">How entities are removed from chunks.</param>
        /// <param name="allocationPolicy">How archetypes select/reuse chunks for insertion.</param>
        public World(
            int initialCapacity = 1024,
            int chunkCapacity = WorldState.DefaultChunkCapacity,
            StorageRemovalPolicy removalPolicy = StorageRemovalPolicy.SwapBack,
            ChunkAllocationPolicy allocationPolicy = ChunkAllocationPolicy.ScanFirstFit,
            int simulationHz = 60,
            bool deterministic = false)
        {
            StoragePolicies policies = new(chunkCapacity, removalPolicy, allocationPolicy);

            State = new WorldState(initialCapacity, policies, simulationHz, deterministic);

            Storage = new StorageService(State);
            Entities = new EntityManager(State);
            Structural = new StructuralChanges(State, Storage, Entities);

            Storage.InitializeEmptyArchetype();

            EnsureEcbInitialized();
        }

        /// <summary>
        /// Deterministic time snapshot.
        /// </summary>
        public WorldTime Time =>
            new(State.CurrentTick, State.TickDelta, State.PresentationDeltaTime, State.SimulationHz);

        internal int CurrentSystemOrder => State.CurrentSystemOrder;

        internal void SetCurrentSystemOrder(int order)
        {
            State.CurrentSystemOrder = order;
        }

        internal void SetPresentationDeltaTime(float deltaTime)
        {
            State.PresentationDeltaTime = deltaTime;
        }
    }
}
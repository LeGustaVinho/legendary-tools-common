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
        /// Initializes a new instance of the <see cref="World"/> class.
        /// </summary>
        /// <param name="initialCapacity">Initial entity capacity.</param>
        public World(int initialCapacity = 1024)
        {
            State = new WorldState(initialCapacity);

            Storage = new StorageService(State);
            Entities = new EntityManager(State);
            Structural = new StructuralChanges(State, Storage, Entities);

            Storage.InitializeEmptyArchetype();

            // ECB setup.
            EnsureEcbInitialized();
        }
    }
}

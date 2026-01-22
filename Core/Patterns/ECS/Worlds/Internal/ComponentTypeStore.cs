using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;
using LegendaryTools.Common.Core.Patterns.ECS.Storage;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    /// <summary>
    /// Owns component type id registration and typed column factories.
    /// </summary>
    internal sealed class ComponentTypeStore
    {
        private readonly ComponentRegistry _registry;
        private readonly Dictionary<int, Func<int, IChunkColumn>> _typedColumnFactories;

        private readonly bool _deterministic;

        public ComponentTypeStore(bool deterministic)
        {
            _deterministic = deterministic;

            _registry = new ComponentRegistry(strictDeterminism: deterministic);
            _typedColumnFactories = new Dictionary<int, Func<int, IChunkColumn>>(128);
        }

        public ComponentManifest Manifest => _registry.Manifest;

        public void RegisterComponent<T>() where T : struct
        {
            ComponentTypeId id = _registry.RegisterOrGet<T>();
            if (_typedColumnFactories.ContainsKey(id.Value)) return;

            _typedColumnFactories.Add(id.Value, cap => new ChunkColumn<T>(cap));
        }

        public ComponentTypeId GetComponentTypeId<T>(bool strictRegisteredOnly) where T : struct
        {
            // When deterministic, always require prior registration. This avoids
            // order-dependent "first touch" registration.
            if (_deterministic)
                strictRegisteredOnly = true;

            if (strictRegisteredOnly)
            {
                if (_registry.TryGetExisting<T>(out ComponentTypeId existing))
                    return existing;

                throw new InvalidOperationException(
                    $"Component {typeof(T).FullName} is not registered. " +
                    "Call World.RegisterComponent<T>() during bootstrap (before simulation).");
            }

            return _registry.RegisterOrGet<T>();
        }

        public IChunkColumn[] CreateColumnsForSignature(int capacity, ArchetypeSignature signature)
        {
            int[] typeIds = signature.TypeIds;

            // NOTE: This array can be larger than typeIds.Length because the pool rounds to power-of-two.
            // Chunk must use ColumnCount = typeIds.Length to avoid touching trailing null entries.
            IChunkColumn[] cols = EcsArrayPool<IChunkColumn>.Rent(typeIds.Length);

            for (int i = 0; i < typeIds.Length; i++)
            {
                cols[i] = CreateTypedColumn(typeIds[i], capacity);
            }

            return cols;
        }

        private IChunkColumn CreateTypedColumn(int typeId, int capacity)
        {
            if (!_typedColumnFactories.TryGetValue(typeId, out Func<int, IChunkColumn> factory))
                throw new InvalidOperationException(
                    $"No column factory registered for ComponentTypeId {typeId}. " +
                    "Call World.RegisterComponent<T>() for each component type used in chunks.");

            return factory(capacity);
        }
    }
}

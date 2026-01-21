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

        public ComponentTypeStore()
        {
            _registry = new ComponentRegistry();
            _typedColumnFactories = new Dictionary<int, Func<int, IChunkColumn>>(128);
        }

        public void RegisterComponent<T>() where T : struct
        {
            ComponentTypeId id = _registry.GetOrCreate<T>();
            if (_typedColumnFactories.ContainsKey(id.Value)) return;

            _typedColumnFactories.Add(id.Value, cap => new ChunkColumn<T>(cap));
        }

        public ComponentTypeId GetComponentTypeId<T>() where T : struct
        {
            return _registry.GetOrCreate<T>();
        }

        public IChunkColumn[] CreateColumnsForSignature(int capacity, ArchetypeSignature signature)
        {
            int[] typeIds = signature.TypeIds;

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
                    $"Call World.RegisterComponent<T>() for each component type used in chunks.");

            return factory(capacity);
        }
    }
}
using System;
using System.Collections.Generic;
using LegendaryTools.Common.Core.Patterns.ECS.Commands;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;
using LegendaryTools.Common.Core.Patterns.ECS.Memory;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    internal sealed class EntityCommandBuffer : ICommandBuffer
    {
        private readonly bool _deterministic;

        private enum CommandType : byte
        {
            CreateEntity = 1,
            DestroyEntity = 2,
            AddComponent = 3,
            RemoveComponent = 4
        }

        private struct Command
        {
            public CommandType Type;

            public int Tick;
            public int SystemOrder;

            public int SortKey;
            public int EntityIndexKey;

            public int ComponentTypeId;
            public int Sequence;

            public Entity Entity;
            public int ValueIndex;
        }

        private interface IValueStore
        {
            void Clear();

            void EnsureCapacity(int capacity);

            int Capacity { get; }

            int Count { get; }

            void ApplyAdd(World world, Entity entity, int valueIndex);
        }

        private sealed class ValueStore<T> : IValueStore where T : struct
        {
            private readonly PooledList<T> _values;

            public ValueStore(int initialCapacity = 64)
            {
                _values = new PooledList<T>(initialCapacity);
            }

            public int Count => _values.Count;

            public int Capacity => _values.Capacity;

            public void EnsureCapacity(int capacity)
            {
                _values.EnsureCapacity(capacity);
            }

            public int Add(in T value)
            {
                _values.Add(value);
                return _values.Count - 1;
            }

            public bool TryAddNoGrow(in T value, out int valueIndex)
            {
                if (!_values.TryAddNoGrow(value))
                {
                    valueIndex = -1;
                    return false;
                }

                valueIndex = _values.Count - 1;
                return true;
            }

            public void Clear()
            {
                _values.Clear();
            }

            public void ApplyAdd(World world, Entity entity, int valueIndex)
            {
                world.InternalAdd(entity, _values[valueIndex]);
            }
        }

        private readonly World _world;

        private readonly PooledList<Command> _commands;
        private readonly Dictionary<int, IValueStore> _valueStoresByTypeId;

        private int _sequence;

        private readonly PooledList<Entity> _tempToReal;

        private int _maxCommands;
        private int _maxTempEntities;

        private static readonly CommandComparer s_commandComparer = new();

        public EntityCommandBuffer(World world, int initialCapacity = 256)
        {
            _world = world;
            _deterministic = world.State.Deterministic;

            _commands = new PooledList<Command>(initialCapacity);
            _valueStoresByTypeId = new Dictionary<int, IValueStore>(128);

            _sequence = 0;
            _tempToReal = new PooledList<Entity>(64);

            _maxCommands = 0;
            _maxTempEntities = 0;
        }

        /// <summary>
        /// Pre-allocates internal buffers to avoid growth (Rent/Return) during hotpaths.
        /// Call this during initialization (outside simulation update).
        /// </summary>
        public void Warmup(int expectedCommands, int expectedTempEntities)
        {
            if (expectedCommands < 0) expectedCommands = 0;
            if (expectedTempEntities < 0) expectedTempEntities = 0;

            _commands.EnsureCapacity(expectedCommands);
            _tempToReal.EnsureCapacity(expectedTempEntities);

            _maxCommands = expectedCommands;
            _maxTempEntities = expectedTempEntities;
        }

        public void WarmupValues<T>(int expectedValues) where T : struct
        {
            if (expectedValues < 0) expectedValues = 0;

            ComponentTypeId typeId = _world.GetComponentTypeId<T>();

            if (!_valueStoresByTypeId.TryGetValue(typeId.Value, out IValueStore store))
            {
                store = new ValueStore<T>(Math.Max(16, expectedValues));
                _valueStoresByTypeId.Add(typeId.Value, store);
            }

            store.EnsureCapacity(expectedValues);
        }

        public void Reset(int tick)
        {
            _commands.Clear();
            _sequence = 0;

            foreach (IValueStore store in _valueStoresByTypeId.Values)
            {
                store.Clear();
            }

            _tempToReal.Clear();

            Tick = tick;
        }

        public int Tick { get; private set; }

        public Entity CreateEntity()
        {
            if (_deterministic && _world.State.IsUpdating)
                throw new InvalidOperationException(
                    "CreateEntity() without sortKey is forbidden in determinism mode because temp indices depend on emission order. " +
                    "Use ECB.CreateEntity(sortKey) with a stable, non-zero sortKey (e.g., ownerEntity.Index or (chunkId<<16)|row).");

            return CreateEntity(int.MinValue);
        }

        public Entity CreateEntity(int sortKey)
        {
            if (_deterministic && _world.State.IsUpdating)
                if (sortKey == 0)
                    throw new InvalidOperationException(
                        "CreateEntity(sortKey=0) is forbidden in determinism mode. " +
                        "Provide a stable, non-zero sortKey (e.g., ownerEntity.Index or (chunkId<<16)|row).");

            EnsureCanAddTempEntity();

            int tempIndex = _tempToReal.Count;
            Entity temp = new(-(tempIndex + 1), 0);

            if (!_tempToReal.TryAddNoGrow(Entity.Invalid) && _deterministic && _world.State.IsUpdating)
                throw new InvalidOperationException(
                    "ECB temp entity capacity exceeded. Call World.WarmupEcb(expectedCommands, expectedTempEntities) " +
                    "with a larger expectedTempEntities value before simulation.");

            int effectiveSortKey = sortKey != 0 ? sortKey : int.MinValue;

            AddCommandNoGrowOrThrow(new Command
            {
                Type = CommandType.CreateEntity,
                Tick = Tick,
                SystemOrder = _world.CurrentSystemOrder,
                SortKey = effectiveSortKey,
                EntityIndexKey = effectiveSortKey,
                ComponentTypeId = 0,
                Sequence = _sequence++,
                Entity = temp,
                ValueIndex = -1
            });

            return temp;
        }

        public void DestroyEntity(Entity entity)
        {
            DestroyEntity(entity, 0);
        }

        public void DestroyEntity(Entity entity, int sortKey)
        {
            EnforceDeterministicSortKeyForTempEntity(entity, sortKey, "DestroyEntity");

            int effectiveSortKey = sortKey != 0 ? sortKey : entity.Index;

            AddCommandNoGrowOrThrow(new Command
            {
                Type = CommandType.DestroyEntity,
                Tick = Tick,
                SystemOrder = _world.CurrentSystemOrder,
                SortKey = effectiveSortKey,
                EntityIndexKey = ComputeEntityIndexKey(entity, effectiveSortKey),
                ComponentTypeId = 0,
                Sequence = _sequence++,
                Entity = entity,
                ValueIndex = -1
            });
        }

        public void Add<T>(Entity entity) where T : struct
        {
            Add(entity, default(T), 0);
        }

        public void Add<T>(Entity entity, int sortKey) where T : struct
        {
            Add(entity, default(T), sortKey);
        }

        public void Add<T>(Entity entity, in T value) where T : struct
        {
            Add(entity, value, 0);
        }

        public void Add<T>(Entity entity, in T value, int sortKey) where T : struct
        {
            EnforceDeterministicSortKeyForTempEntity(entity, sortKey, "Add");

            ComponentTypeId typeId = _world.GetComponentTypeId<T>();

            if (!_valueStoresByTypeId.TryGetValue(typeId.Value, out IValueStore store))
            {
                if (_deterministic && _world.State.IsUpdating)
                    throw new InvalidOperationException(
                        $"ECB value store for {typeof(T).FullName} was not warmed up. " +
                        "Call World.WarmupEcbValues<T>(expectedAddsForType) during bootstrap.");

                store = new ValueStore<T>();
                _valueStoresByTypeId.Add(typeId.Value, store);
            }

            int valueIndex;
            if (_deterministic && _world.State.IsUpdating)
            {
                ValueStore<T> typedStrict = (ValueStore<T>)store;
                if (!typedStrict.TryAddNoGrow(value, out valueIndex))
                    throw new InvalidOperationException(
                        $"ECB value capacity exceeded for {typeof(T).FullName}. " +
                        "Call World.WarmupEcbValues<T>(expectedAddsForType) with a larger value before simulation.");
            }
            else
            {
                ValueStore<T> typed = (ValueStore<T>)store;
                valueIndex = typed.Add(value);
            }

            int effectiveSortKey = sortKey != 0 ? sortKey : entity.Index;

            AddCommandNoGrowOrThrow(new Command
            {
                Type = CommandType.AddComponent,
                Tick = Tick,
                SystemOrder = _world.CurrentSystemOrder,
                SortKey = effectiveSortKey,
                EntityIndexKey = ComputeEntityIndexKey(entity, effectiveSortKey),
                ComponentTypeId = typeId.Value,
                Sequence = _sequence++,
                Entity = entity,
                ValueIndex = valueIndex
            });
        }

        public void Remove<T>(Entity entity) where T : struct
        {
            Remove<T>(entity, 0);
        }

        public void Remove<T>(Entity entity, int sortKey) where T : struct
        {
            EnforceDeterministicSortKeyForTempEntity(entity, sortKey, "Remove");

            ComponentTypeId typeId = _world.GetComponentTypeId<T>();

            int effectiveSortKey = sortKey != 0 ? sortKey : entity.Index;

            AddCommandNoGrowOrThrow(new Command
            {
                Type = CommandType.RemoveComponent,
                Tick = Tick,
                SystemOrder = _world.CurrentSystemOrder,
                SortKey = effectiveSortKey,
                EntityIndexKey = ComputeEntityIndexKey(entity, effectiveSortKey),
                ComponentTypeId = typeId.Value,
                Sequence = _sequence++,
                Entity = entity,
                ValueIndex = -1
            });
        }

        public void Playback()
        {
            int cmdCount = _commands.Count;
            if (cmdCount <= 0) return;

            Array.Sort(_commands.DangerousGetBuffer(), 0, cmdCount, s_commandComparer);

            for (int i = 0; i < cmdCount; i++)
            {
                Command cmd = _commands[i];

                switch (cmd.Type)
                {
                    case CommandType.CreateEntity:
                    {
                        Entity real = _world.InternalCreateEntity();
                        int tempIdx = ToTempIndex(cmd.Entity);
                        _tempToReal[tempIdx] = real;
                        break;
                    }

                    case CommandType.DestroyEntity:
                    {
                        Entity resolved = Resolve(cmd.Entity);
                        _world.InternalDestroyEntity(resolved);
                        break;
                    }

                    case CommandType.AddComponent:
                    {
                        Entity resolved = Resolve(cmd.Entity);
                        if (!_valueStoresByTypeId.TryGetValue(cmd.ComponentTypeId, out IValueStore store))
                            throw new InvalidOperationException("ECB value store missing for AddComponent.");

                        store.ApplyAdd(_world, resolved, cmd.ValueIndex);
                        break;
                    }

                    case CommandType.RemoveComponent:
                    {
                        Entity resolved = Resolve(cmd.Entity);
                        _world.InternalRemoveByTypeId(resolved, cmd.ComponentTypeId);
                        break;
                    }

                    default:
                        throw new InvalidOperationException("Unknown ECB command type.");
                }
            }
        }

        private static int ComputeEntityIndexKey(Entity entity, int effectiveSortKey)
        {
            return entity.Index >= 0 ? entity.Index : effectiveSortKey;
        }

        private void EnsureCanAddTempEntity()
        {
            if (!_deterministic || !_world.State.IsUpdating) return;

            if (_maxTempEntities > 0 && _tempToReal.Count + 1 > _maxTempEntities)
                throw new InvalidOperationException(
                    "ECB temp entity limit exceeded. Call World.WarmupEcb(expectedCommands, expectedTempEntities) " +
                    "with a larger expectedTempEntities value before simulation.");
        }

        private void AddCommandNoGrowOrThrow(in Command cmd)
        {
            if (_deterministic && _world.State.IsUpdating)
            {
                if (_maxCommands > 0 && _commands.Count + 1 > _maxCommands)
                    throw new InvalidOperationException(
                        "ECB command limit exceeded. Call World.WarmupEcb(expectedCommands, expectedTempEntities) " +
                        "with a larger expectedCommands value before simulation.");

                if (!_commands.TryAddNoGrow(cmd))
                    throw new InvalidOperationException(
                        "ECB command capacity exceeded. Call World.WarmupEcb(expectedCommands, expectedTempEntities) " +
                        "with a larger expectedCommands value before simulation.");

                return;
            }

            _commands.Add(cmd);
        }

        private static int ToTempIndex(Entity e)
        {
            int neg = e.Index;
            return -neg - 1;
        }

        private Entity Resolve(Entity e)
        {
            if (e.Index >= 0) return e;

            int tempIdx = ToTempIndex(e);
            if ((uint)tempIdx >= (uint)_tempToReal.Count)
                throw new InvalidOperationException("Invalid temp entity handle.");

            Entity real = _tempToReal[tempIdx];
            if (real.Index < 0) throw new InvalidOperationException("Temp entity was not created before being used.");

            return real;
        }

        private void EnforceDeterministicSortKeyForTempEntity(Entity entity, int sortKey, string apiName)
        {
            if (!_deterministic || !_world.State.IsUpdating) return;

            if (entity.Index < 0 && sortKey == 0)
                throw new InvalidOperationException(
                    $"{apiName} on a temp entity requires a stable, non-zero sortKey in determinism mode. " +
                    "Example: ownerEntity.Index or (chunkId<<16)|row. " +
                    "This prevents order-dependent playback across peers.");
        }

        private sealed class CommandComparer : IComparer<Command>
        {
            public int Compare(Command x, Command y)
            {
                int cmp = x.Tick.CompareTo(y.Tick);
                if (cmp != 0) return cmp;

                cmp = x.SystemOrder.CompareTo(y.SystemOrder);
                if (cmp != 0) return cmp;

                int px = Phase(x.Type);
                int py = Phase(y.Type);
                cmp = px.CompareTo(py);
                if (cmp != 0) return cmp;

                cmp = x.SortKey.CompareTo(y.SortKey);
                if (cmp != 0) return cmp;

                cmp = x.EntityIndexKey.CompareTo(y.EntityIndexKey);
                if (cmp != 0) return cmp;

                cmp = ((int)x.Type).CompareTo((int)y.Type);
                if (cmp != 0) return cmp;

                cmp = x.ComponentTypeId.CompareTo(y.ComponentTypeId);
                if (cmp != 0) return cmp;

                return x.Sequence.CompareTo(y.Sequence);
            }

            private static int Phase(CommandType type)
            {
                return type switch
                {
                    CommandType.CreateEntity => 0,
                    CommandType.RemoveComponent => 1,
                    CommandType.AddComponent => 2,
                    CommandType.DestroyEntity => 3,
                    _ => 2
                };
            }
        }
    }
}
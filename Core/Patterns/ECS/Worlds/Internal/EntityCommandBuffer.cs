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

            public int Worker;
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

        private sealed class WorkerApi : ICommandBuffer
        {
            private readonly EntityCommandBuffer _owner;
            private readonly int _worker;

            public WorkerApi(EntityCommandBuffer owner, int worker)
            {
                _owner = owner;
                _worker = worker;
            }

            public Entity CreateEntity()
            {
                return _owner.CreateEntityInternal(_worker);
            }

            public Entity CreateEntity(int sortKey)
            {
                return _owner.CreateEntityInternal(_worker, sortKey);
            }

            public void DestroyEntity(Entity entity)
            {
                _owner.DestroyEntityInternal(_worker, entity, 0);
            }

            public void DestroyEntity(Entity entity, int sortKey)
            {
                _owner.DestroyEntityInternal(_worker, entity, sortKey);
            }

            public void Add<T>(Entity entity) where T : struct
            {
                _owner.AddInternal(_worker, entity, default(T), 0);
            }

            public void Add<T>(Entity entity, int sortKey) where T : struct
            {
                _owner.AddInternal(_worker, entity, default(T), sortKey);
            }

            public void Add<T>(Entity entity, in T value) where T : struct
            {
                _owner.AddInternal(_worker, entity, value, 0);
            }

            public void Add<T>(Entity entity, in T value, int sortKey) where T : struct
            {
                _owner.AddInternal(_worker, entity, value, sortKey);
            }

            public void Remove<T>(Entity entity) where T : struct
            {
                _owner.RemoveInternal<T>(_worker, entity, 0);
            }

            public void Remove<T>(Entity entity, int sortKey) where T : struct
            {
                _owner.RemoveInternal<T>(_worker, entity, sortKey);
            }
        }

        private struct WorkerBuffer
        {
            public PooledList<Command> Commands;
            public Dictionary<int, IValueStore> ValueStores;

            public int Sequence;
            public int TempCount;
        }

        private WorkerBuffer[] _workers;
        private WorkerApi[] _workerApis;

        private int _workerCount;

        private int _maxCommandsPerWorker;
        private int _maxTempPerWorker;

        private Entity[] _tempToReal;
        private int _tempStride;

        private static readonly CommandComparer s_commandComparer = new();

        public EntityCommandBuffer(World world, int initialCapacity = 256)
        {
            _world = world;
            _deterministic = world.State.Deterministic;

            _workers = Array.Empty<WorkerBuffer>();
            _workerApis = Array.Empty<WorkerApi>();
            _workerCount = 0;

            _maxCommandsPerWorker = 0;
            _maxTempPerWorker = 0;

            _tempToReal = Array.Empty<Entity>();
            _tempStride = 0;

            EnsureWorkers(1);

            _workers[0].Commands.EnsureCapacity(initialCapacity);

            ResetTempMapToInvalid();
        }

        internal void EnsureWorkers(int workerCount)
        {
            if (workerCount < 1) workerCount = 1;
            if (_workerCount == workerCount && _workers.Length == workerCount && _workerApis.Length == workerCount)
                return;

            WorkerBuffer[] newWorkers = new WorkerBuffer[workerCount];
            WorkerApi[] newApis = new WorkerApi[workerCount];

            int copy = Math.Min(_workerCount, workerCount);

            for (int i = 0; i < copy; i++)
            {
                newWorkers[i] = _workers[i];
                newApis[i] = _workerApis[i];
            }

            for (int i = copy; i < workerCount; i++)
            {
                newWorkers[i] = new WorkerBuffer
                {
                    Commands = new PooledList<Command>(256),
                    ValueStores = new Dictionary<int, IValueStore>(128),
                    Sequence = 0,
                    TempCount = 0
                };

                newApis[i] = new WorkerApi(this, i);
            }

            _workers = newWorkers;
            _workerApis = newApis;
            _workerCount = workerCount;

            if (_tempStride > 0)
            {
                int required = _workerCount * _tempStride;
                if (_tempToReal.Length < required)
                {
                    int old = _tempToReal.Length;
                    Array.Resize(ref _tempToReal, required);
                    FillInvalid(old, required - old);
                }
            }
        }

        internal ICommandBuffer GetWorkerBuffer(int workerIndex)
        {
            if ((uint)workerIndex >= (uint)_workerCount)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));

            return _workerApis[workerIndex];
        }

        public void Warmup(int expectedCommands, int expectedTempEntities)
        {
            WarmupParallel(1, expectedCommands, expectedTempEntities);
        }

        public void WarmupParallel(int workerCount, int expectedCommandsPerWorker, int expectedTempEntitiesPerWorker)
        {
            if (workerCount < 1) workerCount = 1;
            if (expectedCommandsPerWorker < 0) expectedCommandsPerWorker = 0;
            if (expectedTempEntitiesPerWorker < 0) expectedTempEntitiesPerWorker = 0;

            EnsureWorkers(workerCount);

            for (int i = 0; i < workerCount; i++)
            {
                _workers[i].Commands.EnsureCapacity(Math.Max(16, expectedCommandsPerWorker));
            }

            _maxCommandsPerWorker = expectedCommandsPerWorker;
            _maxTempPerWorker = expectedTempEntitiesPerWorker;

            _tempStride = Math.Max(1, expectedTempEntitiesPerWorker);
            int required = workerCount * _tempStride;

            if (_tempToReal.Length < required)
            {
                int old = _tempToReal.Length;
                Array.Resize(ref _tempToReal, required);
                FillInvalid(old, required - old);
            }

            ResetTempMapToInvalid();
        }

        public void WarmupValues<T>(int expectedValues) where T : struct
        {
            WarmupValuesParallel<T>(1, expectedValues);
        }

        public void WarmupValuesParallel<T>(int workerCount, int expectedValuesPerWorker) where T : struct
        {
            if (workerCount < 1) workerCount = 1;
            if (expectedValuesPerWorker < 0) expectedValuesPerWorker = 0;

            EnsureWorkers(workerCount);

            for (int w = 0; w < workerCount; w++)
            {
                WarmupValuesWorker<T>(w, expectedValuesPerWorker);
            }
        }

        private void WarmupValuesWorker<T>(int worker, int expectedValues) where T : struct
        {
            if (expectedValues < 0) expectedValues = 0;

            EnsureWorkers(Math.Max(_workerCount, worker + 1));

            ComponentTypeId typeId = _world.GetComponentTypeId<T>();

            if (!_workers[worker].ValueStores.TryGetValue(typeId.Value, out IValueStore store))
            {
                store = new ValueStore<T>(Math.Max(16, expectedValues));
                _workers[worker].ValueStores.Add(typeId.Value, store);
            }

            store.EnsureCapacity(expectedValues);
        }

        public void Reset(int tick)
        {
            for (int w = 0; w < _workerCount; w++)
            {
                _workers[w].Commands.Clear();
                _workers[w].Sequence = 0;
                _workers[w].TempCount = 0;

                foreach (IValueStore store in _workers[w].ValueStores.Values)
                {
                    store.Clear();
                }
            }

            ResetTempMapToInvalid();

            Tick = tick;
        }

        public int Tick { get; private set; }

        public Entity CreateEntity()
        {
            return CreateEntityInternal(0);
        }

        public Entity CreateEntity(int sortKey)
        {
            return CreateEntityInternal(0, sortKey);
        }

        public void DestroyEntity(Entity entity)
        {
            DestroyEntityInternal(0, entity, 0);
        }

        public void DestroyEntity(Entity entity, int sortKey)
        {
            DestroyEntityInternal(0, entity, sortKey);
        }

        public void Add<T>(Entity entity) where T : struct
        {
            AddInternal(0, entity, default(T), 0);
        }

        public void Add<T>(Entity entity, int sortKey) where T : struct
        {
            AddInternal(0, entity, default(T), sortKey);
        }

        public void Add<T>(Entity entity, in T value) where T : struct
        {
            AddInternal(0, entity, value, 0);
        }

        public void Add<T>(Entity entity, in T value, int sortKey) where T : struct
        {
            AddInternal(0, entity, value, sortKey);
        }

        public void Remove<T>(Entity entity) where T : struct
        {
            RemoveInternal<T>(0, entity, 0);
        }

        public void Remove<T>(Entity entity, int sortKey) where T : struct
        {
            RemoveInternal<T>(0, entity, sortKey);
        }

        private Entity CreateEntityInternal(int worker)
        {
            if (_deterministic && _world.State.IsUpdating)
                throw new InvalidOperationException(
                    "CreateEntity() without sortKey is forbidden in determinism mode because temp indices depend on emission order. " +
                    "Use ECB.CreateEntity(sortKey) with a stable, non-zero sortKey (e.g., ownerEntity.Index or (chunkId<<16)|row).");

            return CreateEntityInternal(worker, int.MinValue);
        }

        private Entity CreateEntityInternal(int worker, int sortKey)
        {
            if (_deterministic && _world.State.IsUpdating && sortKey == 0)
                throw new InvalidOperationException(
                    "CreateEntity(sortKey=0) is forbidden in determinism mode. " +
                    "Provide a stable, non-zero sortKey (e.g., ownerEntity.Index or (chunkId<<16)|row).");

            EnsureTempStrideForParallel(worker);

            int local = _workers[worker].TempCount++;
            if (_deterministic && _world.State.IsUpdating && _maxTempPerWorker > 0 && local >= _maxTempPerWorker)
                throw new InvalidOperationException(
                    "ECB temp entity limit exceeded for worker. Call World.WarmupEcbParallel(workerCount, expectedCommandsPerWorker, expectedTempEntitiesPerWorker) " +
                    "with a larger expectedTempEntitiesPerWorker value before simulation.");

            int globalTempIndex = worker * _tempStride + local;
            Entity temp = new(-(globalTempIndex + 1), 0);

            int effectiveSortKey = sortKey != 0 ? sortKey : int.MinValue;

            // IMPORTANT (Determinism hardening):
            // If multiple CreateEntity commands share the same SortKey, order must not fall back to Worker/Sequence.
            // Use the deterministic temp index as EntityIndexKey to break ties before Worker/Sequence.
            int createEntityIndexKey = globalTempIndex;

            AddCommandNoGrowOrThrow(worker, new Command
            {
                Type = CommandType.CreateEntity,
                Tick = Tick,
                SystemOrder = _world.CurrentSystemOrder,
                SortKey = effectiveSortKey,
                EntityIndexKey = createEntityIndexKey,
                ComponentTypeId = 0,
                Worker = worker,
                Sequence = _workers[worker].Sequence++,
                Entity = temp,
                ValueIndex = -1
            });

            return temp;
        }

        private void DestroyEntityInternal(int worker, Entity entity, int sortKey)
        {
            EnforceDeterministicSortKeyForTempEntity(entity, sortKey, "DestroyEntity");

            int effectiveSortKey = sortKey != 0 ? sortKey : entity.Index;

            AddCommandNoGrowOrThrow(worker, new Command
            {
                Type = CommandType.DestroyEntity,
                Tick = Tick,
                SystemOrder = _world.CurrentSystemOrder,
                SortKey = effectiveSortKey,
                EntityIndexKey = ComputeEntityIndexKey(entity, effectiveSortKey),
                ComponentTypeId = 0,
                Worker = worker,
                Sequence = _workers[worker].Sequence++,
                Entity = entity,
                ValueIndex = -1
            });
        }

        private void AddInternal<T>(int worker, Entity entity, in T value, int sortKey) where T : struct
        {
            EnforceDeterministicSortKeyForTempEntity(entity, sortKey, "Add");

            ComponentTypeId typeId = _world.GetComponentTypeId<T>();

            if (!_workers[worker].ValueStores.TryGetValue(typeId.Value, out IValueStore store))
            {
                if (_deterministic && _world.State.IsUpdating)
                    throw new InvalidOperationException(
                        $"ECB value store for {typeof(T).FullName} was not warmed up for worker {worker}. " +
                        "Call World.WarmupEcbValuesParallel<T>(workerCount, expectedAddsPerWorker) during bootstrap.");

                store = new ValueStore<T>();
                _workers[worker].ValueStores.Add(typeId.Value, store);
            }

            int valueIndex;
            if (_deterministic && _world.State.IsUpdating)
            {
                ValueStore<T> typedStrict = (ValueStore<T>)store;
                if (!typedStrict.TryAddNoGrow(value, out valueIndex))
                    throw new InvalidOperationException(
                        $"ECB value capacity exceeded for {typeof(T).FullName} on worker {worker}. " +
                        "Increase expectedAddsPerWorker in World.WarmupEcbValuesParallel<T>().");
            }
            else
            {
                ValueStore<T> typed = (ValueStore<T>)store;
                valueIndex = typed.Add(value);
            }

            int effectiveSortKey = sortKey != 0 ? sortKey : entity.Index;

            AddCommandNoGrowOrThrow(worker, new Command
            {
                Type = CommandType.AddComponent,
                Tick = Tick,
                SystemOrder = _world.CurrentSystemOrder,
                SortKey = effectiveSortKey,
                EntityIndexKey = ComputeEntityIndexKey(entity, effectiveSortKey),
                ComponentTypeId = typeId.Value,
                Worker = worker,
                Sequence = _workers[worker].Sequence++,
                Entity = entity,
                ValueIndex = valueIndex
            });
        }

        private void RemoveInternal<T>(int worker, Entity entity, int sortKey) where T : struct
        {
            EnforceDeterministicSortKeyForTempEntity(entity, sortKey, "Remove");

            ComponentTypeId typeId = _world.GetComponentTypeId<T>();

            int effectiveSortKey = sortKey != 0 ? sortKey : entity.Index;

            AddCommandNoGrowOrThrow(worker, new Command
            {
                Type = CommandType.RemoveComponent,
                Tick = Tick,
                SystemOrder = _world.CurrentSystemOrder,
                SortKey = effectiveSortKey,
                EntityIndexKey = ComputeEntityIndexKey(entity, effectiveSortKey),
                ComponentTypeId = typeId.Value,
                Worker = worker,
                Sequence = _workers[worker].Sequence++,
                Entity = entity,
                ValueIndex = -1
            });
        }

        public void Playback()
        {
            int total = 0;
            for (int w = 0; w < _workerCount; w++)
            {
                total += _workers[w].Commands.Count;
            }

            if (total <= 0) return;

            Command[] merged = EcsArrayPool<Command>.Rent(total);
            int write = 0;

            for (int w = 0; w < _workerCount; w++)
            {
                PooledList<Command> cmds = _workers[w].Commands;
                Command[] buf = cmds.DangerousGetBuffer();
                int count = cmds.Count;

                Array.Copy(buf, 0, merged, write, count);
                write += count;
            }

            Array.Sort(merged, 0, total, s_commandComparer);

            for (int i = 0; i < total; i++)
            {
                Command cmd = merged[i];

                switch (cmd.Type)
                {
                    case CommandType.CreateEntity:
                    {
                        Entity real = _world.InternalCreateEntity();
                        int tempIdx = ToTempIndex(cmd.Entity);

                        EnsureTempMapCapacity(tempIdx + 1);

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

                        int worker = cmd.Worker;
                        if ((uint)worker >= (uint)_workerCount)
                            throw new InvalidOperationException("ECB worker index out of range during playback.");

                        if (!_workers[worker].ValueStores.TryGetValue(cmd.ComponentTypeId, out IValueStore store))
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

            EcsArrayPool<Command>.Return(merged, false);
        }

        private static int ComputeEntityIndexKey(Entity entity, int effectiveSortKey)
        {
            return entity.Index >= 0 ? entity.Index : effectiveSortKey;
        }

        private void AddCommandNoGrowOrThrow(int worker, in Command cmd)
        {
            if (_deterministic && _world.State.IsUpdating)
            {
                if (_maxCommandsPerWorker > 0 && _workers[worker].Commands.Count + 1 > _maxCommandsPerWorker)
                    throw new InvalidOperationException(
                        "ECB command limit exceeded for worker. Call World.WarmupEcbParallel(workerCount, expectedCommandsPerWorker, expectedTempEntitiesPerWorker) " +
                        "with a larger expectedCommandsPerWorker value before simulation.");

                if (!_workers[worker].Commands.TryAddNoGrow(cmd))
                    throw new InvalidOperationException(
                        "ECB command capacity exceeded for worker. Warm up more capacity before simulation.");

                return;
            }

            _workers[worker].Commands.Add(cmd);
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
            if ((uint)tempIdx >= (uint)_tempToReal.Length)
                throw new InvalidOperationException("Invalid temp entity handle.");

            Entity real = _tempToReal[tempIdx];
            if (real.Index < 0)
                throw new InvalidOperationException("Temp entity was not created before being used.");

            return real;
        }

        private void EnsureTempStrideForParallel(int worker)
        {
            if (_tempStride <= 0)
            {
                if (_deterministic && _world.State.IsUpdating && _workerCount > 1)
                    throw new InvalidOperationException(
                        "Parallel temp entities require WarmupEcbParallel(workerCount, expectedCommandsPerWorker, expectedTempEntitiesPerWorker).");

                _tempStride = 1024;

                int required = _workerCount * _tempStride;
                if (_tempToReal.Length < required)
                {
                    int old = _tempToReal.Length;
                    Array.Resize(ref _tempToReal, required);
                    FillInvalid(old, required - old);
                }

                ResetTempMapToInvalid();
            }

            int required2 = (worker + 1) * _tempStride;
            if (_tempToReal.Length < required2)
            {
                if (_deterministic && _world.State.IsUpdating)
                    throw new InvalidOperationException(
                        "Temp entity map exceeded in determinism mode. Increase expectedTempEntitiesPerWorker in WarmupEcbParallel().");

                int old = _tempToReal.Length;
                Array.Resize(ref _tempToReal, required2);
                FillInvalid(old, required2 - old);
            }
        }

        private void EnsureTempMapCapacity(int required)
        {
            if (required <= _tempToReal.Length) return;

            if (_deterministic && _world.State.IsUpdating)
                throw new InvalidOperationException(
                    "Temp entity map capacity exceeded in determinism mode. Increase expectedTempEntitiesPerWorker in WarmupEcbParallel().");

            int old = _tempToReal.Length;
            Array.Resize(ref _tempToReal, required);
            FillInvalid(old, required - old);
        }

        private void ResetTempMapToInvalid()
        {
            if (_tempToReal == null || _tempToReal.Length == 0) return;
            FillInvalid(0, _tempToReal.Length);
        }

        private void FillInvalid(int start, int count)
        {
            if (count <= 0) return;

            Entity invalid = Entity.Invalid;
            int end = start + count;

            for (int i = start; i < end; i++)
            {
                _tempToReal[i] = invalid;
            }
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

                cmp = x.Worker.CompareTo(y.Worker);
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
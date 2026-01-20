using System;
using System.Collections.Generic;

using LegendaryTools.Common.Core.Patterns.ECS.Commands;
using LegendaryTools.Common.Core.Patterns.ECS.Components;
using LegendaryTools.Common.Core.Patterns.ECS.Entities;

namespace LegendaryTools.Common.Core.Patterns.ECS.Worlds.Internal
{
    /// <summary>
    /// MVP ECB: single-thread, no sorting, but API is shaped for future deterministic ordering.
    /// </summary>
    internal sealed class EntityCommandBuffer : ICommandBuffer
    {
        private enum CommandType : byte
        {
            CreateEntity = 1,
            DestroyEntity = 2,
            AddComponent = 3,
            RemoveComponent = 4,
        }

        private struct Command
        {
            public CommandType Type;

            // Determinism keys (MVP: unused but reserved for later).
            public int Tick;
            public int SystemOrder;
            public int SortKey;
            public int Sequence;

            public Entity Entity;

            // For Add/Remove.
            public int ComponentTypeId;

            // For Add: index into a per-type value store.
            public int ValueIndex;
        }

        private interface IValueStore
        {
            int Count { get; }

            void Clear();

            void ApplyAdd(World world, Entity entity, int valueIndex);
        }

        private sealed class ValueStore<T> : IValueStore where T : struct
        {
            private readonly List<T> _values;

            public ValueStore(int initialCapacity = 64)
            {
                _values = new List<T>(initialCapacity);
            }

            public int Count => _values.Count;

            public int Add(in T value)
            {
                _values.Add(value);
                return _values.Count - 1;
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

        private readonly List<Command> _commands;
        private readonly Dictionary<int, IValueStore> _valueStoresByTypeId;

        private int _sequence;

        // Temp entity mapping:
        // Temp entities have negative indices: -(tempIndex + 1)
        private readonly List<Entity> _tempToReal;

        public EntityCommandBuffer(World world, int initialCapacity = 256)
        {
            _world = world;

            _commands = new List<Command>(initialCapacity);
            _valueStoresByTypeId = new Dictionary<int, IValueStore>(128);

            _sequence = 0;
            _tempToReal = new List<Entity>(64);
        }

        public void Reset(int tick)
        {
            _commands.Clear();
            _sequence = 0;

            // Keep stores allocated; just clear values for reuse.
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
            // Create temp handle.
            int tempIndex = _tempToReal.Count;
            Entity temp = new Entity(-(tempIndex + 1), 0);

            _tempToReal.Add(Entity.Invalid);

            _commands.Add(new Command
            {
                Type = CommandType.CreateEntity,
                Tick = Tick,
                SystemOrder = 0,
                SortKey = 0,
                Sequence = _sequence++,
                Entity = temp,
                ComponentTypeId = 0,
                ValueIndex = -1,
            });

            return temp;
        }

        public void DestroyEntity(Entity entity)
        {
            _commands.Add(new Command
            {
                Type = CommandType.DestroyEntity,
                Tick = Tick,
                SystemOrder = 0,
                SortKey = 0,
                Sequence = _sequence++,
                Entity = entity,
                ComponentTypeId = 0,
                ValueIndex = -1,
            });
        }

        public void Add<T>(Entity entity) where T : struct
        {
            Add(entity, default(T));
        }

        public void Add<T>(Entity entity, in T value) where T : struct
        {
            ComponentTypeId typeId = _world.GetComponentTypeId<T>();

            if (!_valueStoresByTypeId.TryGetValue(typeId.Value, out IValueStore store))
            {
                store = new ValueStore<T>();
                _valueStoresByTypeId.Add(typeId.Value, store);
            }

            ValueStore<T> typed = (ValueStore<T>)store;
            int valueIndex = typed.Add(value);

            _commands.Add(new Command
            {
                Type = CommandType.AddComponent,
                Tick = Tick,
                SystemOrder = 0,
                SortKey = 0,
                Sequence = _sequence++,
                Entity = entity,
                ComponentTypeId = typeId.Value,
                ValueIndex = valueIndex,
            });
        }

        public void Remove<T>(Entity entity) where T : struct
        {
            ComponentTypeId typeId = _world.GetComponentTypeId<T>();

            _commands.Add(new Command
            {
                Type = CommandType.RemoveComponent,
                Tick = Tick,
                SystemOrder = 0,
                SortKey = 0,
                Sequence = _sequence++,
                Entity = entity,
                ComponentTypeId = typeId.Value,
                ValueIndex = -1,
            });
        }

        public void Playback()
        {
            // MVP: commands are executed in recording order.
            // Future: stable sort by (Tick, SystemOrder, SortKey, Sequence).
            for (int i = 0; i < _commands.Count; i++)
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
                        {
                            throw new InvalidOperationException("ECB value store missing for AddComponent.");
                        }

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

        private static int ToTempIndex(Entity e)
        {
            // Temp indices are negative: -(tempIndex + 1)
            int neg = e.Index;
            return -neg - 1;
        }

        private Entity Resolve(Entity e)
        {
            if (e.Index >= 0)
            {
                return e;
            }

            int tempIdx = ToTempIndex(e);
            if ((uint)tempIdx >= (uint)_tempToReal.Count)
            {
                throw new InvalidOperationException("Invalid temp entity handle.");
            }

            Entity real = _tempToReal[tempIdx];
            if (real.Index < 0)
            {
                throw new InvalidOperationException("Temp entity was not created before being used.");
            }

            return real;
        }
    }
}

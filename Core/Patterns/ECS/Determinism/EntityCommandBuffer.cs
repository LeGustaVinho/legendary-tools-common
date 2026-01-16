#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LegendaryTools.Common.Core.Patterns.ECS.Determinism
{
    /// <summary>
    /// Deterministic Entity Command Buffer (ECB) for structural changes.
    /// </summary>
    /// <remarks>
    /// Goals:
    /// - Prohibit direct structural changes during simulation; record them here and apply at fixed sync points.
    /// - Be thread-ready via per-worker buffers (even if executed single-thread today).
    /// - Deterministic playback by sorting commands by (Tick, SystemOrder, SortKey, Sequence) with stable ordering.
    ///
    /// Removal policy:
    /// - This ECS uses swap-back removal inside chunks (see <see cref="Chunk.RemoveRowSwapBack"/>).
    /// - Determinism is achieved by applying structural changes via ordered ECB playback.
    /// </remarks>
    public sealed class EntityCommandBuffer
    {
        private readonly ThreadBuffer[] _buffers;

        /// <summary>
        /// Initializes the ECB with a fixed number of worker buffers.
        /// </summary>
        public EntityCommandBuffer(int workerCount, int initialCommandsPerWorker = 256)
        {
            if (workerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(workerCount));

            if (initialCommandsPerWorker < 0)
                initialCommandsPerWorker = 0;

            _buffers = new ThreadBuffer[workerCount];
            for (int i = 0; i < _buffers.Length; i++)
            {
                _buffers[i] = new ThreadBuffer(i, initialCommandsPerWorker);
            }
        }

        /// <summary>
        /// Gets the number of worker buffers.
        /// </summary>
        public int WorkerCount => _buffers.Length;

        /// <summary>
        /// Clears all buffers (keeps capacities).
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _buffers.Length; i++)
            {
                _buffers[i].Clear();
            }
        }

        /// <summary>
        /// Creates a writer bound to a worker buffer and a fixed (tick, systemOrder) context.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Writer CreateWriter(int tick, int systemOrder, int workerIndex)
        {
            if ((uint)workerIndex >= (uint)_buffers.Length)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));

            return new Writer(this, tick, systemOrder, workerIndex);
        }

        /// <summary>
        /// Plays back all recorded commands into the world in deterministic order.
        /// </summary>
        public void Playback(World world)
        {
            if (world is null) throw new ArgumentNullException(nameof(world));

            int total = 0;
            for (int i = 0; i < _buffers.Length; i++)
            {
                total += _buffers[i].Count;
            }

            if (total == 0)
                return;

            EntityCommand[] rented = ArrayPool<EntityCommand>.Shared.Rent(total);

            try
            {
                // Merge in deterministic order: workerIndex ascending.
                int write = 0;
                for (int i = 0; i < _buffers.Length; i++)
                {
                    write = _buffers[i].CopyTo(rented, write);
                }

                Span<EntityCommand> span = rented.AsSpan(0, total);

                // Stable sort by (Tick, SystemOrder, SortKey, Sequence).
                StableSort.Sort(span, EntityCommandComparer.Instance);

                // Apply in order.
                world.BeginEcbPlayback();
                try
                {
                    for (int i = 0; i < span.Length; i++)
                    {
                        Apply(world, span[i]);
                    }
                }
                finally
                {
                    world.EndEcbPlayback();
                }

                // Clear buffers after successful playback.
                for (int i = 0; i < _buffers.Length; i++)
                {
                    _buffers[i].Clear();
                }
            }
            finally
            {
                ArrayPool<EntityCommand>.Shared.Return(rented, false);
            }
        }

        private static void Apply(World world, in EntityCommand cmd)
        {
            switch (cmd.Kind)
            {
                case EntityCommandKind.CreateEntity:
                    world.InternalCreateEntityForEcb(cmd.CreatedEntity, cmd.SortKey);
                    break;

                case EntityCommandKind.DestroyEntity:
                    world.InternalDestroyEntityForEcb(cmd.Entity);
                    break;

                case EntityCommandKind.AddComponent:
                    world.InternalAddComponentForEcb(cmd.Entity, cmd.ComponentId);
                    break;

                case EntityCommandKind.RemoveComponent:
                    world.InternalRemoveComponentForEcb(cmd.Entity, cmd.ComponentId);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown ECB command kind: {cmd.Kind}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ThreadBuffer GetBuffer(int workerIndex)
        {
            return _buffers[workerIndex];
        }

        /// <summary>
        /// Lightweight writer that records commands into a worker buffer.
        /// </summary>
        public readonly struct Writer
        {
            private readonly EntityCommandBuffer _ecb;
            private readonly int _tick;
            private readonly int _systemOrder;
            private readonly int _workerIndex;

            internal Writer(EntityCommandBuffer ecb, int tick, int systemOrder, int workerIndex)
            {
                _ecb = ecb;
                _tick = tick;
                _systemOrder = systemOrder;
                _workerIndex = workerIndex;
            }

            /// <summary>
            /// Records a CreateEntity command.
            /// </summary>
            /// <remarks>
            /// Current minimal behavior:
            /// - Playback will create a new entity deterministically in playback order.
            /// - The provided <paramref name="createdEntity"/> is reserved for future "entity reservation" support.
            /// </remarks>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void CreateEntity(Entity createdEntity, long sortKey)
            {
                ThreadBuffer b = _ecb.GetBuffer(_workerIndex);
                int seq = b.NextSequence();

                b.Add(new EntityCommand(
                    EntityCommandKind.CreateEntity,
                    _tick, _systemOrder, sortKey, seq,
                    createdEntity,
                    default,
                    createdEntity));
            }

            /// <summary>
            /// Records a DestroyEntity command.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void DestroyEntity(Entity e, long sortKey)
            {
                ThreadBuffer b = _ecb.GetBuffer(_workerIndex);
                int seq = b.NextSequence();

                b.Add(new EntityCommand(
                    EntityCommandKind.DestroyEntity,
                    _tick, _systemOrder, sortKey, seq,
                    e,
                    default,
                    default));
            }

            /// <summary>
            /// Records an Add&lt;T&gt; command.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add<T>(World world, Entity e, long sortKey) where T : struct
            {
                if (world is null) throw new ArgumentNullException(nameof(world));

                ComponentTypeId id = world.Components.GetId(typeof(T));

                ThreadBuffer b = _ecb.GetBuffer(_workerIndex);
                int seq = b.NextSequence();

                b.Add(new EntityCommand(
                    EntityCommandKind.AddComponent,
                    _tick, _systemOrder, sortKey, seq,
                    e,
                    id,
                    default));
            }

            /// <summary>
            /// Records a Remove&lt;T&gt; command.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Remove<T>(World world, Entity e, long sortKey) where T : struct
            {
                if (world is null) throw new ArgumentNullException(nameof(world));

                ComponentTypeId id = world.Components.GetId(typeof(T));

                ThreadBuffer b = _ecb.GetBuffer(_workerIndex);
                int seq = b.NextSequence();

                b.Add(new EntityCommand(
                    EntityCommandKind.RemoveComponent,
                    _tick, _systemOrder, sortKey, seq,
                    e,
                    id,
                    default));
            }
        }

        private sealed class ThreadBuffer
        {
            private readonly int _workerIndex;
            private readonly List<EntityCommand> _commands;
            private int _sequence;

            public ThreadBuffer(int workerIndex, int initialCapacity)
            {
                _workerIndex = workerIndex;
                _commands = new List<EntityCommand>(Math.Max(0, initialCapacity));
                _sequence = 0;
            }

            public int Count => _commands.Count;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int NextSequence()
            {
                // Sequence is local per worker buffer and used as the last explicit sort key.
                // Stability across worker buffers is guaranteed by:
                // - Deterministic merge order (workerIndex ascending)
                // - Stable sort implementation (StableSort)
                return _sequence++;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(in EntityCommand command)
            {
                _commands.Add(command);
            }

            public int CopyTo(EntityCommand[] dst, int dstIndex)
            {
                for (int i = 0; i < _commands.Count; i++)
                {
                    dst[dstIndex++] = _commands[i];
                }

                return dstIndex;
            }

            public void Clear()
            {
                _commands.Clear();
                _sequence = 0;
            }

            public override string ToString()
            {
                return $"ThreadBuffer(worker={_workerIndex}, cmds={_commands.Count})";
            }
        }

        private sealed class EntityCommandComparer : IComparer<EntityCommand>
        {
            public static readonly EntityCommandComparer Instance = new();

            public int Compare(EntityCommand x, EntityCommand y)
            {
                int c = x.Tick.CompareTo(y.Tick);
                if (c != 0) return c;

                c = x.SystemOrder.CompareTo(y.SystemOrder);
                if (c != 0) return c;

                c = x.SortKey.CompareTo(y.SortKey);
                if (c != 0) return c;

                return x.Sequence.CompareTo(y.Sequence);
            }
        }
    }
}
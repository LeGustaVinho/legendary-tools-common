using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeterministicFixedPoint;

namespace LegendaryTools.ModifierSystem
{
    /// <summary>
    /// Explicit value registry used by the deterministic save codec. Gameplay payloads
    /// are never serialized through reflection: every non-built-in type must register
    /// a stable ID and typed reader/writer pair.
    /// </summary>
    public sealed class SimulationValueCodecRegistry
    {
        private interface IValueCodec
        {
            string Id { get; }
            Type ValueType { get; }
            void Write(BinaryWriter writer, object value);
            object Read(BinaryReader reader);
        }

        private sealed class ValueCodec<T> : IValueCodec
        {
            private readonly Action<BinaryWriter, T> _write;
            private readonly Func<BinaryReader, T> _read;
            public string Id { get; }
            public Type ValueType => typeof(T);

            public ValueCodec(string id, Action<BinaryWriter, T> write, Func<BinaryReader, T> read)
            {
                Id = id;
                _write = write;
                _read = read;
            }

            public void Write(BinaryWriter writer, object value) => _write(writer, (T)value);
            public object Read(BinaryReader reader) => _read(reader);
        }

        private readonly Dictionary<Type, IValueCodec> _byType =
            new Dictionary<Type, IValueCodec>();
        private readonly Dictionary<string, IValueCodec> _byId =
            new Dictionary<string, IValueCodec>(StringComparer.Ordinal);

        public SimulationValueCodecRegistry()
        {
            Register<bool>("bool", (writer, value) => writer.Write(value), reader => reader.ReadBoolean());
            Register<byte>("u8", (writer, value) => writer.Write(value), reader => reader.ReadByte());
            Register<sbyte>("i8", (writer, value) => writer.Write(value), reader => reader.ReadSByte());
            Register<short>("i16", (writer, value) => writer.Write(value), reader => reader.ReadInt16());
            Register<ushort>("u16", (writer, value) => writer.Write(value), reader => reader.ReadUInt16());
            Register<int>("i32", (writer, value) => writer.Write(value), reader => reader.ReadInt32());
            Register<uint>("u32", (writer, value) => writer.Write(value), reader => reader.ReadUInt32());
            Register<long>("i64", (writer, value) => writer.Write(value), reader => reader.ReadInt64());
            Register<ulong>("u64", (writer, value) => writer.Write(value), reader => reader.ReadUInt64());
            Register<DetS32>("fixed.i32.3", (writer, value) => writer.Write(value.Raw),
                reader => DetS32.FromRaw(reader.ReadInt32()));
            Register<DetU32>("fixed.u32.3", (writer, value) => writer.Write(value.Raw),
                reader => DetU32.FromRaw(reader.ReadUInt32()));
            Register<DetS64>("fixed.i64.3", (writer, value) => writer.Write(value.Raw),
                reader => DetS64.FromRaw(reader.ReadInt64()));
            Register<DetU64>("fixed.u64.3", (writer, value) => writer.Write(value.Raw),
                reader => DetU64.FromRaw(reader.ReadUInt64()));
            Register<decimal>("decimal", (writer, value) => writer.Write(value), reader => reader.ReadDecimal());
            Register<char>("char", (writer, value) => writer.Write(value), reader => reader.ReadChar());
            Register<string>("string", WriteString, ReadString);
            Register<Guid>("guid", (writer, value) => writer.Write(value.ToByteArray()),
                reader => new Guid(ReadExactBytes(reader, 16)));
            Register<byte[]>("bytes", WriteBytes, ReadBytes);
        }

        public void Register<T>(string id, Action<BinaryWriter, T> write, Func<BinaryReader, T> read)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Codec ID is required.", nameof(id));
            if (write == null) throw new ArgumentNullException(nameof(write));
            if (read == null) throw new ArgumentNullException(nameof(read));
            if (_byType.ContainsKey(typeof(T)))
                throw new InvalidOperationException($"A value codec is already registered for {typeof(T).FullName}.");
            if (_byId.ContainsKey(id))
                throw new InvalidOperationException($"Value codec ID {id} is already registered.");
            var codec = new ValueCodec<T>(id, write, read);
            _byType.Add(typeof(T), codec);
            _byId.Add(id, codec);
        }

        internal void WriteValue(BinaryWriter writer, object value)
        {
            if (value == null)
            {
                writer.Write(false);
                return;
            }
            writer.Write(true);
            Type type = value.GetType();
            if (!_byType.TryGetValue(type, out IValueCodec codec))
                throw new InvalidOperationException(
                    $"No deterministic value codec is registered for {type.FullName}.");
            WriteString(writer, codec.Id);
            codec.Write(writer, value);
        }

        internal object ReadValue(BinaryReader reader)
        {
            if (!reader.ReadBoolean()) return null;
            string id = ReadString(reader);
            if (!_byId.TryGetValue(id, out IValueCodec codec))
                throw new InvalidDataException($"Save uses unregistered value codec {id}.");
            return codec.Read(reader);
        }

        internal void WriteType(BinaryWriter writer, Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!_byType.TryGetValue(type, out IValueCodec codec))
                throw new InvalidOperationException(
                    $"No deterministic value codec is registered for {type.FullName}.");
            WriteString(writer, codec.Id);
        }

        internal Type ReadType(BinaryReader reader)
        {
            string id = ReadString(reader);
            if (!_byId.TryGetValue(id, out IValueCodec codec))
                throw new InvalidDataException($"Save uses unregistered value codec {id}.");
            return codec.ValueType;
        }

        internal static void WriteString(BinaryWriter writer, string value)
        {
            if (value == null)
            {
                writer.Write(-1);
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        internal static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length == -1) return null;
            if (length < 0 || length > 268435456)
                throw new InvalidDataException("Invalid string length in simulation save.");
            return Encoding.UTF8.GetString(ReadExactBytes(reader, length));
        }

        private static void WriteBytes(BinaryWriter writer, byte[] value)
        {
            writer.Write(value.Length);
            writer.Write(value);
        }

        private static byte[] ReadBytes(BinaryReader reader)
        {
            int length = ReadCount(reader);
            return ReadExactBytes(reader, length);
        }

        internal static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 100000000)
                throw new InvalidDataException("Invalid collection count in simulation save.");
            return count;
        }

        internal static byte[] ReadExactBytes(BinaryReader reader, int length)
        {
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }
    }

    /// <summary>
    /// Versioned deterministic binary format for <see cref="SimulationSaveState"/>.
    /// The codec does not own gameplay schema; domain state, modifier parameters,
    /// trigger payloads, history payloads, counters, and variables use the explicit
    /// <see cref="SimulationValueCodecRegistry"/>.
    /// </summary>
    public sealed class SimulationSaveBinaryCodec
    {
        private const uint Magic = 0x534D544C; // LTMS
        private const int FormatVersion = 1;
        public SimulationValueCodecRegistry Values { get; }

        public SimulationSaveBinaryCodec(SimulationValueCodecRegistry values = null) =>
            Values = values ?? new SimulationValueCodecRegistry();

        public byte[] Serialize(SimulationSaveState save)
        {
            using (var stream = new MemoryStream())
            {
                Write(stream, save);
                return stream.ToArray();
            }
        }

        public SimulationSaveState Deserialize(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using (var stream = new MemoryStream(bytes, false)) return Read(stream);
        }

        public void Write(Stream stream, SimulationSaveState save)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (!stream.CanWrite) throw new ArgumentException("Stream must be writable.", nameof(stream));
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Magic);
                writer.Write(FormatVersion);
                WriteRuntime(writer, save.Runtime ?? throw new InvalidDataException("Runtime state is required."));
                Values.WriteValue(writer, save.Domain);
            }
        }

        public SimulationSaveState Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Invalid simulation save header.");
                int version = reader.ReadInt32();
                if (version != FormatVersion)
                    throw new InvalidDataException($"Unsupported simulation save version {version}.");
                var save = new SimulationSaveState
                {
                    Runtime = ReadRuntime(reader),
                    Domain = Values.ReadValue(reader)
                };
                if (stream.CanSeek && stream.Position != stream.Length)
                    throw new InvalidDataException("Simulation save contains trailing data.");
                return save;
            }
        }

        private void WriteRuntime(BinaryWriter writer, SimulationRuntimeState state)
        {
            writer.Write(state.CurrentTick);
            writer.Write(state.NextEntityId);
            writer.Write(state.NextModifierSequence);
            writer.Write(state.NextContributionSequence);
            writer.Write(state.RandomState);
            writer.Write(state.NextTriggerRegistrationId);
            WriteGuids(writer, state.CompletedEffectExecutions);
            WriteGuids(writer, state.CompensatedEffectExecutions);

            writer.Write(state.Modifiers.Count);
            foreach (ModifierInstanceState modifier in state.Modifiers) WriteModifier(writer, modifier);
            writer.Write(state.Capabilities.Count);
            foreach (CapabilitySlotState capability in state.Capabilities) WriteCapability(writer, capability);
            writer.Write(state.Counters.Count);
            foreach (CounterState counter in state.Counters) WriteCounter(writer, counter);
            writer.Write(state.Variables.Count);
            foreach (VariableState variable in state.Variables) WriteVariable(writer, variable);
            writer.Write(state.Capacities.Count);
            foreach (CapacityState capacity in state.Capacities) WriteCapacity(writer, capacity);
            writer.Write(state.RandomStreams.Count);
            foreach (RandomStreamState random in state.RandomStreams)
            {
                SimulationValueCodecRegistry.WriteString(writer, random.Id);
                writer.Write(random.State);
            }
            writer.Write(state.AttributeHistories.Count);
            foreach (AttributeHistoryState history in state.AttributeHistories)
                WriteAttributeHistory(writer, history);
            writer.Write(state.Triggers.Count);
            foreach (PersistentTriggerState trigger in state.Triggers)
            {
                SimulationValueCodecRegistry.WriteString(writer, trigger.DefinitionId);
                Values.WriteValue(writer, trigger.State);
                writer.Write(trigger.IsActive);
                SimulationValueCodecRegistry.WriteString(writer, trigger.Explanation);
            }
            writer.Write(state.HistoryStreams.Count);
            foreach (HistoryStreamState history in state.HistoryStreams) WriteHistoryStream(writer, history);
            writer.Write(state.Collections.Count);
            foreach (DeclarativeCollectionState collection in state.Collections)
            {
                SimulationValueCodecRegistry.WriteString(writer, collection.DefinitionId);
                writer.Write(collection.OwnerEntityId);
                WriteLongs(writer, collection.BaseItemEntityIds);
            }
        }

        private SimulationRuntimeState ReadRuntime(BinaryReader reader)
        {
            var state = new SimulationRuntimeState
            {
                CurrentTick = reader.ReadInt64(),
                NextEntityId = reader.ReadInt64(),
                NextModifierSequence = reader.ReadInt64(),
                NextContributionSequence = reader.ReadInt64(),
                RandomState = reader.ReadUInt64(),
                NextTriggerRegistrationId = reader.ReadInt64()
            };
            foreach (Guid id in ReadGuids(reader)) state.AddCompletedExecution(id);
            foreach (Guid id in ReadGuids(reader)) state.AddCompensatedExecution(id);
            int count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++) state.AddModifier(ReadModifier(reader));
            count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++) state.AddCapability(ReadCapability(reader));
            count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++) state.AddCounter(ReadCounter(reader));
            count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++) state.AddVariable(ReadVariable(reader));
            count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++) state.AddCapacity(ReadCapacity(reader));
            count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++)
                state.AddRandomStream(new RandomStreamState
                {
                    Id = SimulationValueCodecRegistry.ReadString(reader),
                    State = reader.ReadUInt64()
                });
            count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++) state.AddAttributeHistory(ReadAttributeHistory(reader));
            count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++)
                state.AddTrigger(new PersistentTriggerState
                {
                    DefinitionId = SimulationValueCodecRegistry.ReadString(reader),
                    State = Values.ReadValue(reader),
                    IsActive = reader.ReadBoolean(),
                    Explanation = SimulationValueCodecRegistry.ReadString(reader)
                });
            count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++) state.AddHistoryStream(ReadHistoryStream(reader));
            count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++)
            {
                var collection = new DeclarativeCollectionState
                {
                    DefinitionId = SimulationValueCodecRegistry.ReadString(reader),
                    OwnerEntityId = reader.ReadInt64()
                };
                foreach (long id in ReadLongs(reader)) collection.AddBaseItem(new EntityId(id));
                state.AddCollection(collection);
            }
            return state;
        }

        private void WriteModifier(BinaryWriter writer, ModifierInstanceState value)
        {
            writer.Write(value.InstanceId.ToByteArray());
            SimulationValueCodecRegistry.WriteString(writer, value.DefinitionId);
            writer.Write(value.SourceEntityId);
            Values.WriteValue(writer, value.Parameters);
            writer.Write(value.AppliedTick);
            WriteNullableLong(writer, value.ExpirationTick);
            SimulationValueCodecRegistry.WriteString(writer, value.StackingKey);
            writer.Write(value.IsActive);
            writer.Write((int)value.SourceConditionEvaluation);
            writer.Write(value.Bindings.Count);
            foreach (ModifierBindingState binding in value.Bindings)
            {
                writer.Write(binding.BindingIndex);
                writer.Write(binding.Targets.Count);
                foreach (ModifierTargetState target in binding.Targets)
                {
                    writer.Write(target.EntityId);
                    Values.WriteValue(writer, target.SnapshotMagnitude);
                    writer.Write(target.Sequence);
                    writer.Write(target.Applied);
                    WriteNullableGuid(writer, target.ContributionId);
                    WriteNullableInt(writer, target.CapabilityDecision.HasValue
                        ? (int?)target.CapabilityDecision.Value : null);
                }
            }
        }

        private ModifierInstanceState ReadModifier(BinaryReader reader)
        {
            var value = new ModifierInstanceState
            {
                InstanceId = new Guid(SimulationValueCodecRegistry.ReadExactBytes(reader, 16)),
                DefinitionId = SimulationValueCodecRegistry.ReadString(reader),
                SourceEntityId = reader.ReadInt64(),
                Parameters = Values.ReadValue(reader),
                AppliedTick = reader.ReadInt64(),
                ExpirationTick = ReadNullableLong(reader),
                StackingKey = SimulationValueCodecRegistry.ReadString(reader),
                IsActive = reader.ReadBoolean(),
                SourceConditionEvaluation = (ConditionEvaluationState)reader.ReadInt32()
            };
            int bindingCount = SimulationValueCodecRegistry.ReadCount(reader);
            for (int bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
            {
                var binding = new ModifierBindingState { BindingIndex = reader.ReadInt32() };
                int targetCount = SimulationValueCodecRegistry.ReadCount(reader);
                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                {
                    long entityId = reader.ReadInt64();
                    object snapshotMagnitude = Values.ReadValue(reader);
                    long sequence = reader.ReadInt64();
                    bool applied = reader.ReadBoolean();
                    Guid? contributionId = ReadNullableGuid(reader);
                    int? decision = ReadNullableInt(reader);
                    binding.AddTarget(new ModifierTargetState
                    {
                        EntityId = entityId,
                        SnapshotMagnitude = snapshotMagnitude,
                        Sequence = sequence,
                        Applied = applied,
                        ContributionId = contributionId,
                        CapabilityDecision = decision.HasValue
                            ? (CapabilityContribution?)((CapabilityContribution)decision.Value)
                            : null
                    });
                }
                value.AddBinding(binding);
            }
            return value;
        }

        private static void WriteCapability(BinaryWriter writer, CapabilitySlotState value)
        {
            SimulationValueCodecRegistry.WriteString(writer, value.DefinitionId);
            writer.Write(value.OwnerEntityId);
            writer.Write(value.Contributions.Count);
            foreach (CapabilityContributionState contribution in value.Contributions)
            {
                writer.Write(contribution.Id.ToByteArray());
                writer.Write((int)contribution.Decision);
                writer.Write(contribution.Priority);
                WriteNullableLong(writer, contribution.SourceEntityId);
                SimulationValueCodecRegistry.WriteString(writer, contribution.Source);
                SimulationValueCodecRegistry.WriteString(writer, contribution.SourceKey);
            }
        }

        private static CapabilitySlotState ReadCapability(BinaryReader reader)
        {
            var value = new CapabilitySlotState
            {
                DefinitionId = SimulationValueCodecRegistry.ReadString(reader),
                OwnerEntityId = reader.ReadInt64()
            };
            int count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++)
                value.AddContribution(new CapabilityContributionState
                {
                    Id = new Guid(SimulationValueCodecRegistry.ReadExactBytes(reader, 16)),
                    Decision = (CapabilityContribution)reader.ReadInt32(),
                    Priority = reader.ReadInt32(),
                    SourceEntityId = ReadNullableLong(reader),
                    Source = SimulationValueCodecRegistry.ReadString(reader),
                    SourceKey = SimulationValueCodecRegistry.ReadString(reader)
                });
            return value;
        }

        private void WriteCounter(BinaryWriter writer, CounterState value)
        {
            SimulationValueCodecRegistry.WriteString(writer, value.KeyId);
            writer.Write(value.OwnerEntityId);
            Values.WriteType(writer, value.ValueType);
            Values.WriteValue(writer, value.Value);
        }

        private CounterState ReadCounter(BinaryReader reader) => new CounterState
        {
            KeyId = SimulationValueCodecRegistry.ReadString(reader),
            OwnerEntityId = reader.ReadInt64(),
            ValueType = Values.ReadType(reader),
            Value = Values.ReadValue(reader)
        };

        private void WriteVariable(BinaryWriter writer, VariableState value)
        {
            Values.WriteType(writer, value.ValueType);
            SimulationValueCodecRegistry.WriteString(writer, value.KeyId);
            writer.Write((int)value.Scope);
            WriteNullableLong(writer, value.OwnerEntityId);
            WriteNullableInt(writer, value.OwnerKind.HasValue ? (int?)value.OwnerKind.Value : null);
            SimulationValueCodecRegistry.WriteString(writer, value.OwnerKey);
            Values.WriteValue(writer, value.Value);
        }

        private VariableState ReadVariable(BinaryReader reader)
        {
            Type valueType = Values.ReadType(reader);
            string keyId = SimulationValueCodecRegistry.ReadString(reader);
            VariableScope scope = (VariableScope)reader.ReadInt32();
            long? ownerEntityId = ReadNullableLong(reader);
            int? ownerKind = ReadNullableInt(reader);
            return new VariableState
            {
                ValueType = valueType,
                KeyId = keyId,
                Scope = scope,
                OwnerEntityId = ownerEntityId,
                OwnerKind = ownerKind.HasValue
                    ? (VariableOwnerKind?)((VariableOwnerKind)ownerKind.Value)
                    : null,
                OwnerKey = SimulationValueCodecRegistry.ReadString(reader),
                Value = Values.ReadValue(reader)
            };
        }

        private static void WriteCapacity(BinaryWriter writer, CapacityState value)
        {
            SimulationValueCodecRegistry.WriteString(writer, value.DefinitionId);
            writer.Write(value.OwnerEntityId);
            writer.Write(value.BaseCapacity);
            WriteLongs(writer, value.ItemEntityIds);
            WriteLongs(writer, value.DisabledEntityIds);
        }

        private static CapacityState ReadCapacity(BinaryReader reader)
        {
            var value = new CapacityState
            {
                DefinitionId = SimulationValueCodecRegistry.ReadString(reader),
                OwnerEntityId = reader.ReadInt64(),
                BaseCapacity = reader.ReadInt32()
            };
            foreach (long id in ReadLongs(reader)) value.AddItem(new EntityId(id));
            foreach (long id in ReadLongs(reader)) value.AddDisabled(new EntityId(id));
            return value;
        }

        private void WriteAttributeHistory(BinaryWriter writer, AttributeHistoryState value)
        {
            writer.Write(value.OwnerEntityId);
            SimulationValueCodecRegistry.WriteString(writer, value.DefinitionId);
            writer.Write(value.LastSampleTick);
            writer.Write(value.SummaryCount);
            Values.WriteValue(writer, value.SummaryFirst);
            Values.WriteValue(writer, value.SummaryLast);
            Values.WriteValue(writer, value.SummaryMinimum);
            Values.WriteValue(writer, value.SummaryMaximum);
            Values.WriteValue(writer, value.SummaryTotal);
            writer.Write(value.SummaryHasTotal);
            writer.Write(value.SummaryObservedCount);
            WriteHistoricalRecords(writer, value.Records);
        }

        private AttributeHistoryState ReadAttributeHistory(BinaryReader reader)
        {
            var value = new AttributeHistoryState
            {
                OwnerEntityId = reader.ReadInt64(),
                DefinitionId = SimulationValueCodecRegistry.ReadString(reader),
                LastSampleTick = reader.ReadInt64(),
                SummaryCount = reader.ReadInt64(),
                SummaryFirst = Values.ReadValue(reader),
                SummaryLast = Values.ReadValue(reader),
                SummaryMinimum = Values.ReadValue(reader),
                SummaryMaximum = Values.ReadValue(reader),
                SummaryTotal = Values.ReadValue(reader),
                SummaryHasTotal = reader.ReadBoolean(),
                SummaryObservedCount = reader.ReadInt64()
            };
            foreach (HistoricalValueState record in ReadHistoricalRecords(reader)) value.AddRecord(record);
            return value;
        }

        private void WriteHistoryStream(BinaryWriter writer, HistoryStreamState value)
        {
            SimulationValueCodecRegistry.WriteString(writer, value.DefinitionId);
            WriteNullableInt(writer, value.OwnerKind.HasValue ? (int?)value.OwnerKind.Value : null);
            SimulationValueCodecRegistry.WriteString(writer, value.OwnerKey);
            writer.Write(value.LastSampleTick);
            writer.Write(value.SummaryCount);
            Values.WriteValue(writer, value.SummaryFirst);
            Values.WriteValue(writer, value.SummaryLast);
            Values.WriteValue(writer, value.SummaryMinimum);
            Values.WriteValue(writer, value.SummaryMaximum);
            Values.WriteValue(writer, value.SummaryTotal);
            writer.Write(value.SummaryHasTotal);
            writer.Write(value.SummaryObservedCount);
            writer.Write(value.HasCurrentState);
            Values.WriteValue(writer, value.CurrentState);
            writer.Write(value.CurrentStateEnteredTick);
            WriteHistoricalRecords(writer, value.Records);
            writer.Write(value.StateDurations.Count);
            var durations = new List<Tuple<byte[], long>>(value.StateDurations.Count);
            foreach (HistoryStateDuration duration in value.StateDurations)
            {
                using (var stream = new MemoryStream())
                using (var stateWriter = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    Values.WriteValue(stateWriter, duration.State);
                    stateWriter.Flush();
                    durations.Add(Tuple.Create(stream.ToArray(), duration.Ticks));
                }
            }
            durations.Sort((left, right) => CompareBytes(left.Item1, right.Item1));
            foreach (Tuple<byte[], long> duration in durations)
            {
                writer.Write(duration.Item1);
                writer.Write(duration.Item2);
            }
        }

        private HistoryStreamState ReadHistoryStream(BinaryReader reader)
        {
            string definitionId = SimulationValueCodecRegistry.ReadString(reader);
            int? ownerKind = ReadNullableInt(reader);
            var value = new HistoryStreamState
            {
                DefinitionId = definitionId,
                OwnerKind = ownerKind.HasValue
                    ? (VariableOwnerKind?)((VariableOwnerKind)ownerKind.Value)
                    : null,
                OwnerKey = SimulationValueCodecRegistry.ReadString(reader),
                LastSampleTick = reader.ReadInt64(),
                SummaryCount = reader.ReadInt64(),
                SummaryFirst = Values.ReadValue(reader),
                SummaryLast = Values.ReadValue(reader),
                SummaryMinimum = Values.ReadValue(reader),
                SummaryMaximum = Values.ReadValue(reader),
                SummaryTotal = Values.ReadValue(reader),
                SummaryHasTotal = reader.ReadBoolean(),
                SummaryObservedCount = reader.ReadInt64(),
                HasCurrentState = reader.ReadBoolean(),
                CurrentState = Values.ReadValue(reader),
                CurrentStateEnteredTick = reader.ReadInt64()
            };
            foreach (HistoricalValueState record in ReadHistoricalRecords(reader)) value.AddRecord(record);
            int count = SimulationValueCodecRegistry.ReadCount(reader);
            for (int index = 0; index < count; index++)
                value.AddStateDuration(new HistoryStateDuration
                {
                    State = Values.ReadValue(reader),
                    Ticks = reader.ReadInt64()
                });
            return value;
        }

        private void WriteHistoricalRecords(BinaryWriter writer, IReadOnlyList<HistoricalValueState> values)
        {
            writer.Write(values.Count);
            foreach (HistoricalValueState value in values)
            {
                writer.Write(value.Tick);
                Values.WriteValue(writer, value.Previous);
                Values.WriteValue(writer, value.Current);
                SimulationValueCodecRegistry.WriteString(writer, value.Reason);
            }
        }

        private List<HistoricalValueState> ReadHistoricalRecords(BinaryReader reader)
        {
            int count = SimulationValueCodecRegistry.ReadCount(reader);
            var values = new List<HistoricalValueState>(count);
            for (int index = 0; index < count; index++)
                values.Add(new HistoricalValueState
                {
                    Tick = reader.ReadInt64(),
                    Previous = Values.ReadValue(reader),
                    Current = Values.ReadValue(reader),
                    Reason = SimulationValueCodecRegistry.ReadString(reader)
                });
            return values;
        }

        private static void WriteGuids(BinaryWriter writer, IReadOnlyList<Guid> values)
        {
            writer.Write(values.Count);
            foreach (Guid value in values) writer.Write(value.ToByteArray());
        }

        private static List<Guid> ReadGuids(BinaryReader reader)
        {
            int count = SimulationValueCodecRegistry.ReadCount(reader);
            var values = new List<Guid>(count);
            for (int index = 0; index < count; index++)
                values.Add(new Guid(SimulationValueCodecRegistry.ReadExactBytes(reader, 16)));
            return values;
        }

        private static void WriteLongs(BinaryWriter writer, IReadOnlyList<long> values)
        {
            writer.Write(values.Count);
            foreach (long value in values) writer.Write(value);
        }

        private static List<long> ReadLongs(BinaryReader reader)
        {
            int count = SimulationValueCodecRegistry.ReadCount(reader);
            var values = new List<long>(count);
            for (int index = 0; index < count; index++) values.Add(reader.ReadInt64());
            return values;
        }

        private static void WriteNullableLong(BinaryWriter writer, long? value)
        {
            writer.Write(value.HasValue);
            if (value.HasValue) writer.Write(value.Value);
        }

        private static long? ReadNullableLong(BinaryReader reader) =>
            reader.ReadBoolean() ? (long?)reader.ReadInt64() : null;

        private static void WriteNullableInt(BinaryWriter writer, int? value)
        {
            writer.Write(value.HasValue);
            if (value.HasValue) writer.Write(value.Value);
        }

        private static int? ReadNullableInt(BinaryReader reader) =>
            reader.ReadBoolean() ? (int?)reader.ReadInt32() : null;

        private static void WriteNullableGuid(BinaryWriter writer, Guid? value)
        {
            writer.Write(value.HasValue);
            if (value.HasValue) writer.Write(value.Value.ToByteArray());
        }

        private static Guid? ReadNullableGuid(BinaryReader reader) =>
            reader.ReadBoolean()
                ? (Guid?)new Guid(SimulationValueCodecRegistry.ReadExactBytes(reader, 16))
                : null;

        private static int CompareBytes(byte[] left, byte[] right)
        {
            int length = Math.Min(left.Length, right.Length);
            for (int index = 0; index < length; index++)
            {
                int comparison = left[index].CompareTo(right[index]);
                if (comparison != 0) return comparison;
            }
            return left.Length.CompareTo(right.Length);
        }
    }
}

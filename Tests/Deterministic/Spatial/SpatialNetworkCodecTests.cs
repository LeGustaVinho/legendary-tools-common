using System;
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    public unsafe class SpatialNetworkCodecTests
    {
        private readonly struct ConfiguredIntCodec : IDeterministicCodec<int>, ISpatialCodecFingerprint
        {
            public ulong CodecFingerprint { get; }
            public ConfiguredIntCodec(ulong fingerprint) { CodecFingerprint = fingerprint; }
            public uint SchemaId => default(Int32Policy).SchemaId;
            public ushort Version => default(Int32Policy).Version;
            public int Measure(in int value) => default(Int32Policy).Measure(value);
            public DeterministicStatus TryWrite(ref DeterministicWriter writer, in int value) => default(Int32Policy).TryWrite(ref writer, value);
            public DeterministicStatus TryRead(ref DeterministicReader reader, out int value) => default(Int32Policy).TryRead(ref reader, out value);
        }
        [OneTimeSetUp] public void Initialize()
        { if (!FixedRuntime.IsInitialized) FixedRuntime.Initialize(); if (!FixedBoundsRuntime.IsInitialized) FixedBoundsRuntime.Initialize(); }
        private static FixedBoundsNetworkProfile Profile(int dimensions, FixedCodec codec, int precision = -1, bool lossy = false)
        {
            var axis = new FixedNetworkProfile(FixedRuntime.State.Settings, FixedKind.Signed64, codec, precision, lossy,
                codec == FixedCodec.BitPacked ? unchecked((ulong)-1000L) : 0, codec == FixedCodec.BitPacked ? 1000UL : 0);
            return new FixedBoundsNetworkProfile(new FixedVectorNetworkProfile(axis, dimensions));
        }
        private static void Exercise<TBounds, TAccess>(TBounds bounds, FixedCodec kind, int dimensions)
            where TBounds : unmanaged where TAccess : unmanaged, ISpatialBoundsNetworkAccess<TBounds>
        {
            var codec = new SpatialBoundsNetworkCodec<TBounds, TAccess>(Profile(dimensions, kind));
            byte* packet = stackalloc byte[100]; byte* shortPacket = stackalloc byte[100];
            for (int i = 0; i < 100; i++) packet[i] = 0xa5;
            var writer = new DeterministicWriter(packet, 100, 3); codec.TryWrite(ref writer, bounds).ThrowIfFailure(); int end = writer.Position;
            Assert.AreEqual(3 + codec.Measure(bounds), end);
            var measure = DeterministicWriter.CreateMeasure(); codec.TryWrite(ref measure, bounds).ThrowIfFailure(); Assert.AreEqual(end - 3, measure.Position);
            var reader = new DeterministicReader(packet, end, 3); codec.TryRead(ref reader, out var restored).ThrowIfFailure();
            Assert.IsTrue(default(TAccess).RawEquals(bounds, restored)); Assert.AreEqual(end, reader.Position);
            for (int size = 3; size < end; size++)
            {
                reader = new DeterministicReader(packet, size, 3); Assert.AreEqual(DeterministicStatus.BufferTooSmall, codec.TryRead(ref reader, out _)); Assert.AreEqual(3, reader.Position);
                for (int i = 0; i < 100; i++) shortPacket[i] = 0x5a;
                var shortWriter = new DeterministicWriter(shortPacket, size, 3); Assert.AreEqual(DeterministicStatus.BufferTooSmall, codec.TryWrite(ref shortWriter, bounds));
                Assert.AreEqual(3, shortWriter.Position); for (int i = 0; i < 100; i++) Assert.AreEqual(0x5a, shortPacket[i]);
            }
            packet[3] ^= 1; reader = new DeterministicReader(packet, end, 3);
            Assert.AreEqual(DeterministicStatus.ProfileMismatch, codec.TryRead(ref reader, out _)); Assert.AreEqual(3, reader.Position); packet[3] ^= 1;
            if (kind == FixedCodec.BitPacked && (codec.Profile.PackedBits & 7) != 0)
            {
                packet[end - 1] |= 0x80; reader = new DeterministicReader(packet, end, 3);
                Assert.AreEqual(DeterministicStatus.InvalidEncoding, codec.TryRead(ref reader, out _)); Assert.AreEqual(3, reader.Position);
            }
        }
        [TestCase(FixedCodec.VarInt)] [TestCase(FixedCodec.FixedWidth)] [TestCase(FixedCodec.BitPacked)]
        public void CompactCodecsPreserveRawBoundsAndSignedZeroWithAtomicOffsets(FixedCodec kind)
        {
            var z = Fixed64.NegativeZero;
            Assert.IsTrue(FixedBounds2.TryFromStorage(new FixedVector2(z, Fixed64.Zero), new FixedVector2(Fixed64.FromRaw(4), Fixed64.FromRaw(6)), out var a, out _));
            Assert.IsTrue(FixedBounds3.TryFromStorage(new FixedVector3(z, Fixed64.Zero, z), new FixedVector3(Fixed64.FromRaw(4), Fixed64.FromRaw(6), Fixed64.FromRaw(8)), out var b, out _));
            Exercise<FixedBounds2, SpatialBounds2NetworkAccess>(a, kind, 2); Exercise<FixedBounds3, SpatialBounds3NetworkAccess>(b, kind, 3);
        }
        [Test] public void LossyWritesRequireExplicitAuthoritativeCanonicalizationAndFullProfilesChangeTheBinding()
        {
            int precision = FixedRuntime.State.Settings.DecimalPlaces64;
            Assert.Greater(precision, 0);
            var coarse = new SpatialBoundsNetworkCodec<FixedBounds3, SpatialBounds3NetworkAccess>(Profile(3, FixedCodec.VarInt, precision - 1, true));
            var fine = new SpatialBoundsNetworkCodec<FixedBounds3, SpatialBounds3NetworkAccess>(Profile(3, FixedCodec.VarInt));
            var value = FixedBounds3.FromMinMax(FixedVector3.FromRaw(11, 13, 15), FixedVector3.FromRaw(111, 113, 115));
            byte* bytes = stackalloc byte[100]; var writer = new DeterministicWriter(bytes, 100);
            Assert.AreEqual(DeterministicStatus.InvalidEncoding, coarse.TryWrite(ref writer, value)); Assert.AreEqual(0, writer.Position);
            Assert.IsTrue(coarse.TryCanonicalize(value, out var canonical, out _)); coarse.TryWrite(ref writer, canonical).ThrowIfFailure();
            var reader = new DeterministicReader(bytes, writer.Position); coarse.TryRead(ref reader, out var restored).ThrowIfFailure();
            Assert.IsTrue(default(SpatialBounds3NetworkAccess).RawEquals(canonical, restored));
            using var index = new DeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(8));
            var a = SpatialStateBindings.Managed(1, index, default(Int32Policy), default(Int32Policy), fine, 1, 4096, 8192);
            var b = SpatialStateBindings.Managed(1, index, default(Int32Policy), default(Int32Policy), coarse, 1, 4096, 8192);
            Assert.AreNotEqual(a.Signature, b.Signature); Assert.AreNotEqual(fine.CodecFingerprint, coarse.CodecFingerprint);
            // The same canonical authoritative value is indexed by both peers; quantization is never cache-only.
            index.TryAdd(1, canonical, 0).ThrowIfFailure(); var measured = DeterministicWriter.CreateMeasure();
            index.TryWriteCanonical(ref measured, default(Int32Policy), default(Int32Policy), coarse).ThrowIfFailure();
        }
        [TestCase(true)] [TestCase(false)]
        public void ConfiguredKeyAndPayloadProfilesParticipateInCompatibility(bool key)
        {
            using var index = new DeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(8));
            var original = new ConfiguredIntCodec(11); var changed = new ConfiguredIntCodec(12);
            var a = SpatialStateBindings.Managed3(1, index, original, original, 1, 4096, 8192);
            var b = SpatialStateBindings.Managed3(1, index, key ? changed : original, key ? original : changed, 1, 4096, 8192);
            Assert.AreNotEqual(a.Signature, b.Signature);
#if !FIXEDPOINT_STANDALONE
            var native = new NativeDeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(8), Unity.Collections.Allocator.Persistent);
            try
            {
                Assert.AreEqual(a.Signature, SpatialStateBindings.Native3(1, native, original, original, 1, 4096, 8192).Signature);
                Assert.AreEqual(b.Signature, SpatialStateBindings.Native3(1, native, key ? changed : original, key ? original : changed, 1, 4096, 8192).Signature);
            }
            finally { native.Dispose(); }
#endif
        }
        [Test] public void InvalidConfiguredProfilesAreRejectedBeforeRegistration()
        {
            using var index = new DeterministicSpatialIndex3<int, int, Int32Policy>(new SpatialIndexOptions(8));
            var valid = new ConfiguredIntCodec(11);
            Assert.Throws<ArgumentException>(() => SpatialStateBindings.Managed3(1, index, default(ConfiguredIntCodec), valid, 1, 4096, 8192));
            Assert.Throws<ArgumentException>(() => SpatialStateBindings.Managed3(1, index, valid, default(ConfiguredIntCodec), 1, 4096, 8192));
            Assert.Throws<ArgumentException>(() => SpatialStateBindings.Managed(1, index, valid, valid,
                default(SpatialBoundsNetworkCodec<FixedBounds3, SpatialBounds3NetworkAccess>), 1, 4096, 8192));
        }
    }
}

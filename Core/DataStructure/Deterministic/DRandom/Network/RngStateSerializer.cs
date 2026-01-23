using System;
using System.Buffers.Binary;

namespace LegendaryTools.Common.Core.Patterns.ECS.Random.Network
{
    /// <summary>
    /// Portable serialization helpers for RNG state snapshots.
    /// Useful when you need to sync/record exact RNG state (rare),
    /// e.g., debugging, authoritative rollback checkpoints.
    /// </summary>
    public static class RngStateSerializer
    {
        /// <summary>
        /// Size in bytes for serialized <see cref="RngState"/> (state + inc).
        /// </summary>
        public const int SizeBytes = 16;

        /// <summary>
        /// Writes the RNG state into a buffer in little-endian order.
        /// </summary>
        public static void WriteLittleEndian(RngState state, Span<byte> destination)
        {
            if (destination.Length < SizeBytes)
                throw new ArgumentException($"Destination must be at least {SizeBytes} bytes.", nameof(destination));

            BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(0, 8), state.State);
            BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(8, 8), state.Inc);
        }

        /// <summary>
        /// Reads the RNG state from a buffer in little-endian order.
        /// </summary>
        public static RngState ReadLittleEndian(ReadOnlySpan<byte> source)
        {
            if (source.Length < SizeBytes)
                throw new ArgumentException($"Source must be at least {SizeBytes} bytes.", nameof(source));

            ulong s = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(0, 8));
            ulong inc = BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(8, 8));
            return new RngState(s, inc);
        }
    }
}
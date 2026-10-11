using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace LegendaryTools.FixedPoint
{
    public enum FixedBatchOperation : byte
    {
        Add, Subtract, Multiply, Divide, Remainder, IEEERemainder, FusedMultiplyAdd,
        Abs, Ceiling, Floor, Truncate, Round, Sqrt, Cbrt, Sin, Cos, Tan, Asin, Acos, Atan,
        Sinh, Cosh, Tanh, Asinh, Acosh, Atanh, Exp, Log, Log2, Log10, ReciprocalEstimate, ReciprocalSqrtEstimate
    }

    /// <summary>Synchronous, caller-owned array execution. Scalar semantics are identical in every mode.</summary>
    public static partial class FixedBatch
    {
        public static bool IsBurstAvailable => BurstCompiler.IsEnabled;

        private static ulong Calculate(ulong a, ulong b, ulong c, FixedBatchOperation operation, FixedKind kind, out FixedStatus status)
        {
            if (operation <= FixedBatchOperation.IEEERemainder)
                return FixedKernel.Binary(a, b, kind, FixedRuntime.State, (FixedBinary)operation, out status);
            if (operation == FixedBatchOperation.FusedMultiplyAdd)
                return FixedMathKernel.FusedMultiplyAdd(a, b, c, kind, FixedRuntime.State, out status);
            return FixedMathKernel.Unary(a, kind, FixedRuntime.State, (FixedUnary)((int)operation - (int)FixedBatchOperation.Abs), out status);
        }

        private static unsafe ulong Read<T>(T value, bool wide) where T : unmanaged
        { return wide ? *(ulong*)UnsafeUtility.AddressOf(ref value) : *(uint*)UnsafeUtility.AddressOf(ref value); }
        private static unsafe T Make<T>(ulong raw, bool wide) where T : unmanaged
        {
            T result = default;
            if (wide) *(ulong*)UnsafeUtility.AddressOf(ref result) = raw; else *(uint*)UnsafeUtility.AddressOf(ref result) = (uint)raw;
            return result;
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct BatchJob<T> : IJobParallelFor where T : unmanaged
        {
            [ReadOnly] internal NativeArray<T> A, B, C;
            [WriteOnly] internal NativeArray<T> Output;
            internal FixedKind Kind;
            internal FixedBatchOperation Operation;
            public void Execute(int index) => Evaluate(index, out _);
            internal void Evaluate(int index, out FixedStatus status)
            {
                bool wide = Kind >= FixedKind.Signed64;
                ulong raw = Calculate(Read(A[index], wide), Read(B[index], wide), Read(C[index], wide), Operation, Kind, out status);
                Output[index] = Make<T>(raw, wide);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct BatchStatusJob<T> : IJobParallelFor where T : unmanaged
        {
            internal BatchJob<T> Computation;
            [WriteOnly] internal NativeArray<FixedStatus> Statuses;
            public void Execute(int index)
            { Computation.Evaluate(index, out FixedStatus status); Statuses[index] = status; }
        }

        private static void Execute<T>(NativeArray<T> a, NativeArray<T> b, NativeArray<T> c, NativeArray<T> output,
            NativeArray<FixedStatus> statuses, FixedBatchOperation operation, FixedExecutionMode mode, FixedKind kind) where T : unmanaged
        {
            if (!FixedRuntime.IsInitialized) FixedStatus.Uninitialized.ThrowIfFailure();
            if (operation > FixedBatchOperation.ReciprocalSqrtEstimate || mode > FixedExecutionMode.Burst) throw new ArgumentOutOfRangeException();
            bool needsB = operation <= FixedBatchOperation.FusedMultiplyAdd;
            if (!a.IsCreated || !output.IsCreated || output.Length != a.Length ||
                needsB && (!b.IsCreated || b.Length != a.Length) || operation == FixedBatchOperation.FusedMultiplyAdd && (!c.IsCreated || c.Length != a.Length) ||
                b.IsCreated && b.Length != a.Length || c.IsCreated && c.Length != a.Length || statuses.IsCreated && statuses.Length != a.Length)
                throw new ArgumentException("Batch buffers must have matching lengths; binary operations need B and FMA needs C.");
            if (mode == FixedExecutionMode.Burst && !IsBurstAvailable) FixedStatus.BackendUnavailable.ThrowIfFailure();
            // Native array aliasing can invalidate job safety. A caller wanting in-place execution uses Managed.
            // Unused read-only inputs share A's handle; every scheduled container is initialized.
            var job = new BatchJob<T> { A = a, B = b.IsCreated ? b : a, C = c.IsCreated ? c : a, Output = output, Kind = kind, Operation = operation };
            if (mode == FixedExecutionMode.Managed || !IsBurstAvailable)
            { for (int i = 0; i < a.Length; i++) { job.Evaluate(i, out FixedStatus status); if (statuses.IsCreated) statuses[i] = status; } }
            else if (a.Length != 0)
            {
                if (statuses.IsCreated) new BatchStatusJob<T> { Computation = job, Statuses = statuses }.Schedule(a.Length, 64).Complete();
                else job.Schedule(a.Length, 64).Complete();
            }
        }
    }
}

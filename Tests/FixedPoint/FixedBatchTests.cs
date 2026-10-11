using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedBatchTests
    {
        [OneTimeSetUp] public void Initialize() => FixedRuntime.Initialize();

        [Test]
        public void ManagedAutoAndBurstModesExecuteAllFourTypesWithoutOptionalBuffers()
        {
            foreach (FixedExecutionMode mode in Enum.GetValues(typeof(FixedExecutionMode)))
            {
                using (var a = new NativeArray<Fixed32>(new[] { (Fixed32)2, (Fixed32)3 }, Allocator.TempJob))
                using (var result = new NativeArray<Fixed32>(2, Allocator.TempJob))
                { FixedBatch.Execute(a, result, FixedBatchOperation.Sqrt, mode); Assert.AreEqual(1414, result[0].RawValue); Assert.AreEqual(1732, result[1].RawValue); }
                using (var a = new NativeArray<UFixed32>(new[] { (UFixed32)2U, (UFixed32)3U }, Allocator.TempJob))
                using (var result = new NativeArray<UFixed32>(2, Allocator.TempJob))
                { FixedBatch.Execute(a, result, FixedBatchOperation.Sqrt, mode); Assert.AreEqual(1414U, result[0].RawValue); }
                using (var a = new NativeArray<Fixed64>(new[] { (Fixed64)2L, (Fixed64)3L }, Allocator.TempJob))
                using (var result = new NativeArray<Fixed64>(2, Allocator.TempJob))
                { FixedBatch.Execute(a, result, FixedBatchOperation.Sqrt, mode); Assert.AreEqual(1414L, result[0].RawValue); }
                using (var a = new NativeArray<UFixed64>(new[] { (UFixed64)2UL, (UFixed64)3UL }, Allocator.TempJob))
                using (var result = new NativeArray<UFixed64>(2, Allocator.TempJob))
                { FixedBatch.Execute(a, result, FixedBatchOperation.Sqrt, mode); Assert.AreEqual(1414UL, result[0].RawValue); }
            }
        }

        [Test]
        public void PerElementErrorsAndBackendSelectionAreObservable()
        {
            using (var a = new NativeArray<UFixed64>(new[] { UFixed64.MaxValue, UFixed64.One }, Allocator.TempJob))
            using (var b = new NativeArray<UFixed64>(new[] { UFixed64.One, UFixed64.Zero }, Allocator.TempJob))
            using (var result = new NativeArray<UFixed64>(2, Allocator.TempJob))
            using (var statuses = new NativeArray<FixedStatus>(2, Allocator.TempJob))
            {
                FixedBatch.Execute(a, result, FixedBatchOperation.Divide, FixedExecutionMode.Burst, statuses, b);
                Assert.AreEqual(a[0], result[0]); Assert.AreEqual(FixedStatus.None, statuses[0]);
                Assert.IsTrue(UFixed64.IsPositiveInfinity(result[1])); Assert.AreEqual(FixedStatus.DivisionByZero, statuses[1]);
                bool enabled = BurstCompiler.Options.EnableBurstCompilation;
                try
                {
                    BurstCompiler.Options.EnableBurstCompilation = false;
                    FixedBatch.Execute(a, result, FixedBatchOperation.Abs, FixedExecutionMode.Auto);
                    Assert.AreEqual(a[0], result[0]);
                    Assert.Throws<ArithmeticException>(() => FixedBatch.Execute(a, result, FixedBatchOperation.Abs, FixedExecutionMode.Burst));
                }
                finally { BurstCompiler.Options.EnableBurstCompilation = enabled; }
            }
        }
    }
}

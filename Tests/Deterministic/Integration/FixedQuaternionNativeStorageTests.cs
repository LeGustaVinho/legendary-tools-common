using LegendaryTools.FixedPoint;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.Deterministic.Tests
{
    public sealed unsafe class FixedQuaternionNativeStorageTests
    {
        [BurstCompile(CompileSynchronously=true)]
        private struct WriteJob : IJob
        {
            internal NativeDeterministicList<FixedQuaternion> List;
            internal NativeDeterministicSparseSet<FixedQuaternion> Components;
            internal NativeArray<int> Result;
            public void Execute()
            {
                var q=FixedQuaternion.Euler(10,20,30); Result[0]=(int)List.TrySet(0,q); Result[1]=(int)Components.TrySet(new StableHandle(1,1),q); MarkManaged();
            }
            [BurstDiscard] private void MarkManaged() => Result[2]=1;
        }
        [Test] public void NativeListAndSparseSetRunInBurstAndRestoreRawCanonicalState()
        {
            FixedRuntime.Initialize(); Assert.IsTrue(BurstCompiler.IsEnabled); FixedDeterministicProfile.TryQuaternionFingerprint(out var numeric).ThrowIfFailure();
            var context=new DeterministicRuntimeContext(DeterministicBackend.BurstCompiler,new NativeDeterministicMemoryFactory()); var options=new BufferOptions(4,snapshotSlots:0);
            using (var list=new NativeDeterministicList<FixedQuaternion>(options,Allocator.Persistent))
            using (var components=new NativeDeterministicSparseSet<FixedQuaternion>(options,options,Allocator.Persistent))
            using (var result=new NativeArray<int>(3,Allocator.TempJob))
            using (var registry=new DeterministicStateRegistry(context,new DeterministicCompatibilityProfile(1,numeric,0,1,1),2))
            {
                var original=new FixedQuaternion(Fixed64.NegativeZero,Fixed64.NaN,Fixed64.PositiveInfinity,2); var entity=new StableHandle(1,1);
                list.Add(original); components.Add(entity,original);
                registry.TryRegister(NativeStateBindings.List(1,list,default(FixedQuaternionPolicy),1,1024,1024)).ThrowIfFailure();
                registry.TryRegister(NativeStateBindings.SparseSet(2,components,default(FixedQuaternionPolicy),1,1024,2048)).ThrowIfFailure(); registry.TryFreeze().ThrowIfFailure();
                registry.TryEncode(0,out byte* encoded,out int length).ThrowIfFailure(); var packet=new byte[length]; for(int i=0;i<length;i++) packet[i]=encoded[i];
                new WriteJob {List=list,Components=components,Result=result}.Schedule().Complete(); Assert.AreEqual(0,result[0]); Assert.AreEqual(0,result[1]); Assert.AreEqual(0,result[2]);
                fixed (byte* bytes=packet) {var reader=new DeterministicReader(bytes,length); registry.TryReadCanonical(ref reader,out _).ThrowIfFailure();}
                for(int i=0;i<4;i++) {Assert.AreEqual(original[i].RawValue,list[0][i].RawValue); Assert.AreEqual(original[i].RawValue,components[entity][i].RawValue);}
            }
        }
    }
}

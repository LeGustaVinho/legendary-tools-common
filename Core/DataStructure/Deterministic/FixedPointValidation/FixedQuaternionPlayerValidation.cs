using System;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LegendaryTools.FixedPoint
{
    internal static class FixedQuaternionPlayerValidation
    {
        [BurstCompile(CompileSynchronously=true)]
        private struct CorpusJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<FixedQuaternionReplayCorpus.Sample> Samples;
            internal NativeArray<FixedQuaternionReplayCorpus.Result> Results;
            public void Execute(int index) { var r=FixedQuaternionReplayCorpus.Evaluate(Samples[index]); MarkManaged(ref r.Managed); Results[index]=r; }
            [BurstDiscard] private static void MarkManaged(ref int value) => value=1;
        }
        [BurstCompile(CompileSynchronously=true)]
        private struct SequenceJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<FixedQuaternionSequenceCorpus.Sample> Samples;
            [NativeDisableParallelForRestriction] internal NativeArray<FixedQuaternionSequenceCorpus.Result> Results;
            public void Execute(int index)
            {
                int sequence=Samples.Length-1-index; var sample=Samples[sequence];
                var state=FixedQuaternionSequenceCorpus.Initialize(sample);
                for (int tick=0;tick<FixedQuaternionSequenceCorpus.Steps;tick++)
                {
                    var result=FixedQuaternionSequenceCorpus.Advance(sample,ref state);
                    MarkManaged(ref result.Managed); Results[sequence*FixedQuaternionSequenceCorpus.Steps+tick]=result;
                }
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value=1;
        }
        private struct PublicResult { internal FixedQuaternion Product, Rotation; internal FixedVector3 Vector; internal Fixed64 Angle; internal FixedStatus Status; internal int Managed; }
        [BurstCompile(CompileSynchronously=true)]
        private struct PublicJob : IJob
        {
            internal NativeArray<PublicResult> Results;
            public void Execute()
            {
                var q=new FixedQuaternion(3,0,0,4); var rotation=FixedQuaternion.Euler(10,20,30);
                var r=new PublicResult {Product=q*q,Rotation=rotation,Vector=rotation*FixedVector3.forward,Angle=FixedQuaternion.Angle(FixedQuaternion.identity,rotation,out var status),Status=status};
                MarkManaged(ref r.Managed); Results[0]=r;
            }
            [BurstDiscard] private static void MarkManaged(ref int value) => value=1;
        }
        private static void ValidatePublicApi()
        {
            using (var a=new NativeArray<PublicResult>(1,Allocator.Persistent)) using (var b=new NativeArray<PublicResult>(1,Allocator.Persistent))
            {
                new PublicJob {Results=a}.Execute(); new PublicJob {Results=b}.Schedule().Complete(); var m=a[0]; var n=b[0];
                if (m.Managed!=1 || n.Managed!=0) throw new InvalidOperationException("Quaternion public API did not execute native Burst.");
                for (int i=0;i<4;i++) if (m.Product[i].RawValue!=n.Product[i].RawValue || m.Rotation[i].RawValue!=n.Rotation[i].RawValue) throw new InvalidOperationException("Quaternion public raw mismatch.");
                for (int i=0;i<3;i++) if (m.Vector[i].RawValue!=n.Vector[i].RawValue) throw new InvalidOperationException("Quaternion vector raw mismatch.");
                if (m.Status!=n.Status || m.Angle.RawValue!=n.Angle.RawValue) throw new InvalidOperationException("Quaternion public status/angle mismatch.");
                var expected=FixedRuntime.Settings.QuaternionNormalization==FixedQuaternionNormalizationMode.None ? new FixedQuaternion(24,0,0,7) : new FixedQuaternion(Fixed64.FromRatio(24,25),0,0,Fixed64.FromRatio(7,25));
                if (n.Product!=expected) throw new InvalidOperationException("Frozen quaternion composition policy was not used by the public operator.");
            }
        }
        internal static void RunSequences(out int cases,out int nativeCases,out ulong checksum,string tracePath)
        {
            cases=nativeCases=0; checksum=14695981039346656037UL;
            using (var writer=tracePath==null ? null : new StreamWriter(tracePath))
            using (var samples=new NativeArray<FixedQuaternionSequenceCorpus.Sample>(FixedQuaternionSequenceCorpus.Create(),Allocator.Persistent))
            using (var native=new NativeArray<FixedQuaternionSequenceCorpus.Result>(FixedQuaternionSequenceCorpus.ExpectedCases,Allocator.Persistent))
            {
                if (writer!=null) writer.NewLine="\n";
                new SequenceJob {Samples=samples,Results=native}.Schedule(samples.Length,17).Complete();
                for (int sequence=0;sequence<samples.Length;sequence++)
                {
                    var sample=samples[sequence]; var state=FixedQuaternionSequenceCorpus.Initialize(sample);
                    for (int tick=0;tick<FixedQuaternionSequenceCorpus.Steps;tick++)
                    {
                        int index=sequence*FixedQuaternionSequenceCorpus.Steps+tick;
                        var a=FixedQuaternionSequenceCorpus.Advance(sample,ref state); var b=native[index];
                        if (b.Managed!=0) throw new InvalidOperationException("Quaternion sequence did not execute native Burst.");
                        if (!a.Equals(b)) throw new InvalidOperationException("Quaternion dependent state/status/wire mismatch at step "+index);
                        checksum=FixedQuaternionReplayCorpus.Mix(checksum,FixedQuaternionSequenceCorpus.Hash(b));
                        if (writer!=null) FixedQuaternionSequenceCorpus.WriteTraceRow(writer,index,b);
                    }
                }
                cases=nativeCases=native.Length;
                if (cases!=FixedQuaternionSequenceCorpus.ExpectedCases || checksum!=FixedQuaternionSequenceCorpus.ExpectedChecksum) throw new InvalidOperationException("Quaternion dependent sequence differs from its versioned golden checksum.");
            }
        }
        internal static void Run(out int cases,out int nativeCases,out ulong checksum,string tracePath)
        {
            cases=nativeCases=0; checksum=14695981039346656037UL; ValidatePublicApi();
            using (var writer=tracePath==null ? null : new StreamWriter(tracePath))
            using (var samples=new NativeArray<FixedQuaternionReplayCorpus.Sample>(FixedQuaternionReplayCorpus.Create(),Allocator.Persistent))
            using (var managed=new NativeArray<FixedQuaternionReplayCorpus.Result>(samples.Length,Allocator.Persistent))
            using (var native=new NativeArray<FixedQuaternionReplayCorpus.Result>(samples.Length,Allocator.Persistent))
            {
                if (writer!=null) writer.NewLine="\n";
                var reference=new CorpusJob {Samples=samples,Results=managed}; for (int i=0;i<samples.Length;i++) reference.Execute(i);
                new CorpusJob {Samples=samples,Results=native}.Schedule(samples.Length,17).Complete();
                for (int i=0;i<samples.Length;i++)
                {
                    var a=managed[i]; var b=native[i]; if (a.Managed!=1 || b.Managed!=0) throw new InvalidOperationException("Quaternion corpus did not execute native Burst.");
                    a.Managed=0; if (!a.Equals(b)) throw new InvalidOperationException("Quaternion raw/status/wire mismatch at sample "+i);
                    checksum=FixedQuaternionReplayCorpus.Mix(checksum,FixedQuaternionReplayCorpus.Hash(b)); if (writer!=null) FixedQuaternionReplayCorpus.WriteTraceRow(writer,i,b);
                }
                cases=nativeCases=samples.Length;
                if (cases!=FixedQuaternionReplayCorpus.ExpectedCases || checksum!=FixedQuaternionReplayCorpus.ExpectedChecksum) throw new InvalidOperationException("Quaternion corpus differs from its versioned golden checksum.");
            }
        }
    }
}

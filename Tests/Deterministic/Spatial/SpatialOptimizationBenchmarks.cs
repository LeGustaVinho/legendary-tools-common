using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using LegendaryTools.FixedPoint;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Explicit("Measure non-geometric static batches separately from correctness tests.")]
    public class SpatialOptimizationBenchmarks
    {
        [OneTimeSetUp] public void Initialize()
        {if(!FixedRuntime.IsInitialized)FixedRuntime.Initialize();if(!FixedBoundsRuntime.IsInitialized)FixedBoundsRuntime.Initialize();}

        private static void Measure<TBounds,TAccess,TCodec,TNetworkAccess>(int capacity,bool native,StreamWriter report)
            where TBounds:unmanaged where TAccess:unmanaged,ISpatialBoundsAccess<TBounds> where TCodec:unmanaged,IDeterministicCodec<TBounds>
            where TNetworkAccess:unmanaged,ISpatialBoundsNetworkAccess<TBounds>
        {
            var options=new SpatialIndexOptions(new BufferOptions(capacity,snapshotSlots:4,pageBytes:4096),SpatialIndexStrategy.StaticBvh,rollback:SpatialRollbackPolicy.FastRestore);
            using var owner=new SpatialHotpathOpportunityBenchmarks.Owner<TBounds,TAccess,TCodec,TNetworkAccess>(options,native);
            owner.Populate(capacity,capacity);for(int i=0;i<4;i++)owner.Retain(false);
            var times=new double[7];var allocated=new long[7];long growth=0;
            for(int sample=-2;sample<7;sample++)
            {
                owner.ResetMutationBaseline(32,false);owner.Commands(32,777,dataOnly:true);long before=owner.Stats.UsedBytes;
                long gc=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                owner.Execute(SpatialHotpathOpportunityBenchmarks.Operation.Batch,1);
                long elapsed=Stopwatch.GetTimestamp()-start,bytes=GC.GetAllocatedBytesForCurrentThread()-gc;
                owner.AssertSuccess();owner.AssertCommands(false);
                if(sample>=0){times[sample]=elapsed*1000000.0/Stopwatch.Frequency;allocated[sample]=bytes;growth=Math.Max(growth,owner.Stats.UsedBytes-before);}
            }
            Array.Sort(times);Array.Sort(allocated);
            report.WriteLine(string.Join("\t",native?"Burst scheduled batches":"Managed",FixedBoundsRuntime.Settings.Representation,default(TAccess).Dimensions,capacity,32,4,4096,7,
                times[3].ToString("F3",CultureInfo.InvariantCulture),times[6].ToString("F3",CultureInfo.InvariantCulture),allocated[3],growth,owner.Digest()));report.Flush();
        }
        [TestCase(1024,false)] [TestCase(8192,false)]
#if !FIXEDPOINT_STANDALONE
        [TestCase(1024,true)] [TestCase(8192,true)]
#endif
        [Timeout(1800000)]
        public void StaticBatchThatOnlyChangesData(int capacity,bool native)
        {
            string folder=Environment.GetEnvironmentVariable("LEGENDARY_SPATIAL_HOTPATH_REPORT_DIR")??TestContext.CurrentContext.WorkDirectory;
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,$"spatial-data-batch-{FixedBoundsRuntime.Settings.Representation}-{(native?"burst":"managed")}-{capacity}.tsv");
            using var report=new StreamWriter(path);
            report.WriteLine("backend\trepresentation\tdimensions\tcapacity\tcommands\tsnapshot_slots\tpage_bytes\tsamples\tmedian_us_per_op\tp95_us_per_op\thost_gc_bytes_median_batch\tmax_live_growth_bytes\tdigest");
            Measure<FixedBounds2,SpatialBounds2Access,FixedBounds2Policy,SpatialBounds2NetworkAccess>(capacity,native,report);
            Measure<FixedBounds3,SpatialBounds3Access,FixedBounds3Policy,SpatialBounds3NetworkAccess>(capacity,native,report);
            TestContext.Progress.WriteLine("Static data batch benchmark report: "+path);
        }
    }
}

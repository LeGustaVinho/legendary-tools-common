using System;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;
#if !FIXEDPOINT_STANDALONE
using Unity.Burst;
#endif

namespace LegendaryTools.FixedPoint.Tests.Benchmarks
{
#if !FIXEDPOINT_STANDALONE
    [BurstCompile]
#endif
    [Category("QuaternionBenchmark"), Explicit("Select QuaternionCpuBenchmarks explicitly to measure CPU/memory opportunities.")]
    public sealed unsafe class QuaternionCpuBenchmarks : QuaternionBenchmarkFixture
    {
#if !FIXEDPOINT_STANDALONE
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Kernel(QuaternionBenchmarkSample* inputs,int count,QuaternionBenchmarkSpec* spec,QuaternionBenchmarkResult* result);
        private static Kernel native;
        [BurstCompile(CompileSynchronously=true,DisableDirectCall=true)]
        [AOT.MonoPInvokeCallback(typeof(Kernel))]
        private static void Entry(QuaternionBenchmarkSample* inputs,int count,QuaternionBenchmarkSpec* spec,QuaternionBenchmarkResult* result) => QuaternionBenchmarkWorkload.Run(inputs,count,spec,result);
#endif
        private static QuaternionBenchmarkSpec Spec(int precision=3,FixedMathProfile math=FixedMathProfile.Accurate) => new QuaternionBenchmarkSpec {Numeric=new FixedRuntimeState(new FixedSettings(decimalPlaces64:precision,mathProfile:math)),Queries=1,PacketStride=192};
        private void Batch(QuaternionBenchmarkSample[] data,QuaternionBenchmarkSpec spec,string opportunity,string scenario,long cacheBytes=0,ulong? expected=null)
        {
            QuaternionBenchmarkSpec* active=stackalloc QuaternionBenchmarkSpec[1];*active=spec;
            QuaternionBenchmarkResult* reference=stackalloc QuaternionBenchmarkResult[1];QuaternionBenchmarkResult* measured=stackalloc QuaternionBenchmarkResult[1];
            fixed(QuaternionBenchmarkSample* pin=data)
            {
                QuaternionBenchmarkSample* input=pin;QuaternionBenchmarkWorkload.Run(input,data.Length,active,reference);
                if(expected.HasValue) Assert.AreEqual(expected.Value,reference->Digest,"prototype full raw/status/wire parity");
                int items=data.Length*((spec.Operation>=QuaternionBenchOperation.RotateMany && spec.Operation<=QuaternionBenchOperation.RotateBuildCached)?spec.Queries:1);
                Action verify=()=>{Assert.AreEqual(reference->Digest,measured->Digest);Assert.AreEqual(reference->Status,measured->Status);Assert.AreEqual(reference->Bytes,measured->Bytes);};
                Measure(opportunity,spec.Operation.ToString(),"Managed","preallocated kernel batch + loop/digest",spec.Numeric.Settings.DecimalPlaces64,spec.Numeric.Settings.MathProfile.ToString(),scenario,items,
                    (long)data.Length*sizeof(QuaternionBenchmarkSample),cacheBytes,reference->Bytes,()=>QuaternionBenchmarkWorkload.Run(input,data.Length,active,measured),()=>{verify();Assert.AreEqual(1,measured->Managed);});
#if !FIXEDPOINT_STANDALONE
                Assert.IsTrue(BurstCompiler.IsEnabled);if(native==null)native=BurstCompiler.CompileFunctionPointer<Kernel>(Entry).Invoke;
                native(input,data.Length,active,measured);verify();Assert.AreEqual(0,measured->Managed,"native Burst fallback forbidden");
                Measure(opportunity,spec.Operation.ToString(),"Burst","one function pointer transition + kernel batch/loop/digest",spec.Numeric.Settings.DecimalPlaces64,spec.Numeric.Settings.MathProfile.ToString(),scenario,items,
                    (long)data.Length*sizeof(QuaternionBenchmarkSample),cacheBytes,reference->Bytes,()=>native(input,data.Length,active,measured),()=>{verify();Assert.AreEqual(0,measured->Managed);});
#endif
            }
        }
        private static ulong Digest(QuaternionBenchmarkSample[] data,QuaternionBenchmarkSpec spec)
        {
            QuaternionBenchmarkResult result;fixed(QuaternionBenchmarkSample* input=data)QuaternionBenchmarkWorkload.Run(input,data.Length,&spec,&result);return result.Digest;
        }
        private static BigInteger Integer(FixedBigUInt value)
        {BigInteger result=0;for(int i=15;i>=0;i--)result=(result<<32)+value.Get(i);return result;}
        [Test]
        public void PrototypesKeepQuotientRemainderRoundingOverflowAndSignedZeros()
        {
            ulong seed=0x97fa1264UL;
            foreach(int numeratorBits in new[]{0,1,63,64,127,128,255,256,511,512})
            foreach(int denominatorBits in new[]{1,63,64,127,255,511,512})
            {
                FixedBigUInt numerator=default,denominator=default;
                for(int i=0;i<16;i++){numerator.Set(i,(uint)QuaternionBenchmarkWorkload.Next(ref seed));denominator.Set(i,(uint)QuaternionBenchmarkWorkload.Next(ref seed));}
                numerator=FixedBigUInt.ShiftRight(numerator,512-numeratorBits);denominator=FixedBigUInt.ShiftRight(denominator,512-denominatorBits);
                if(denominator.IsZero)denominator=FixedBigUInt.From(1);
                BigInteger quotient=BigInteger.DivRem(Integer(numerator),Integer(denominator),out var remainder);
                foreach(bool word in new[]{false,true})
                {
                    var actual=QuaternionBenchmarkPrototypes.DivRem(numerator,denominator,out var rem,word);
                    Assert.AreEqual(quotient,Integer(actual));Assert.AreEqual(remainder,Integer(rem));
                    foreach(FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
                    foreach(FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow)))
                    foreach(bool negative in new[]{false,true})
                    {
                        var state=new FixedRuntimeState(new FixedSettings(rounding:rounding,overflow:overflow));var value=new FixedQ(numerator,negative);var status=FixedStatus.None;
                        ulong expected=FixedVectorKernel.RoundRatio(value,denominator,state,ref status);
                        Assert.AreEqual(expected,QuaternionBenchmarkPrototypes.Ratio(value,denominator,state,word,out var candidate));Assert.AreEqual(status,candidate);
                    }
                }
            }
        }
        [Test]
        public void MeasureRationalRoundingWithSinglePassAndWordDenominators()
        {
            foreach(int precision in new[]{3,9,18})
            {
                var spec=Spec(precision);var data=QuaternionBenchmarkWorkload.Create(128,spec.Numeric);
                spec.Operation=QuaternionBenchOperation.Ratio;ulong expected=Digest(data,spec);
                foreach(var operation in new[]{QuaternionBenchOperation.Ratio,QuaternionBenchOperation.RatioOnePass,QuaternionBenchOperation.RatioWord})
                {spec.Operation=operation;Batch(data,spec,"Q1","mixed scale/scale-squared divisors;exact raw/status",expected:expected);}
            }
        }
        [Test]
        public void IsolateGeometryGuardPrecisionAndDegenerateBranchCosts()
        {
            foreach(int precision in new[]{3,18}) foreach(FixedMathProfile math in Enum.GetValues(typeof(FixedMathProfile)))
            {
                var spec=Spec(precision,math);var data=QuaternionBenchmarkWorkload.Create(32,spec.Numeric);
                for(int operation=(int)QuaternionBenchOperation.LoopControl;operation<=(int)QuaternionBenchOperation.RotateTowards;operation++)
                {spec.Operation=(QuaternionBenchOperation)operation;Batch(data,spec,"Q2","generic finite varied inputs; radians");}
                foreach(string branch in new[]{"near-parallel","antipodal","zero-domain","huge-argument"})
                {
                    var edge=QuaternionBenchmarkWorkload.Create(32,spec.Numeric);ulong scale=spec.Numeric.Scale64;
                    for(int i=0;i<edge.Length;i++)
                    {
                        if(branch=="near-parallel") {edge[i].A=new FixedQuaternionBits(0,0,0,scale);edge[i].B=new FixedQuaternionBits(0,1,0,scale);edge[i].V=new FixedVectorBits(1,scale,0);}
                        if(branch=="antipodal") {edge[i].B=new FixedQuaternionBits(unchecked(0UL-edge[i].A.X),unchecked(0UL-edge[i].A.Y),unchecked(0UL-edge[i].A.Z),unchecked(0UL-edge[i].A.W));edge[i].V=new FixedVectorBits(0,unchecked(0UL-scale),0);}
                        if(branch=="zero-domain") {edge[i].A=default;edge[i].V=default;}
                        if(branch=="huge-argument") {edge[i].Amount=0x7ffffffffffffffdUL;edge[i].V=new FixedVectorBits(0x7ffffffffffffffdUL,1,1);}
                    }
                    foreach(var operation in new[]{QuaternionBenchOperation.Angle,QuaternionBenchOperation.Slerp,QuaternionBenchOperation.FromTo,QuaternionBenchOperation.Look,QuaternionBenchOperation.Axis})
                    {spec.Operation=operation;Batch(edge,spec,"Q2",branch+";radians");}
                }
            }
        }
        [Test]
        public void ComparePreparedExactRotationWithConstructionAndAdditionalResidentMemory()
        {
            foreach(int precision in new[]{3,18})
            {
                var spec=Spec(precision);var data=QuaternionBenchmarkWorkload.Create(64,spec.Numeric);
                var caches=new QuaternionBenchmarkPrototypes.PreparedRotation[data.Length];var points=new FixedVectorBits[data.Length*32];ulong seed=0x1123UL;
                for(int i=0;i<data.Length;i++) {caches[i]=QuaternionBenchmarkPrototypes.Prepare(data[i].A,spec.Numeric);for(int j=0;j<32;j++)points[i*32+j]=new FixedVectorBits(QuaternionBenchmarkWorkload.Next(ref seed)%spec.Numeric.Scale64,unchecked(0UL-(ulong)(j+1)),(ulong)(i+1));}
                // Verify all rounding/overflow policies with special vectors/quaternions before measuring the finite fast path.
                foreach(FixedRounding rounding in Enum.GetValues(typeof(FixedRounding))) foreach(FixedOverflow overflow in Enum.GetValues(typeof(FixedOverflow)))
                foreach(var q in new[]{data[0].A,default,FixedQuaternionKernel.NaN,new FixedQuaternionBits(0x7ffffffffffffffdUL,0x8000000000000002UL,1,2)})
                foreach(var v in new[]{points[0],default,new FixedVectorBits(0x7ffffffffffffffeUL,0,0),new FixedVectorBits(0x8000000000000000UL,0,0)})
                {
                    var state=new FixedRuntimeState(new FixedSettings(decimalPlaces64:precision,rounding:rounding,overflow:overflow));var cache=QuaternionBenchmarkPrototypes.Prepare(q,state);
                    var expected=FixedQuaternionKernel.Rotate(q,v,state,out var status);var actual=QuaternionBenchmarkPrototypes.Rotate(cache,v,state,out var candidate);
                    Assert.AreEqual(expected,actual);Assert.AreEqual(status,candidate);
                }
                fixed(QuaternionBenchmarkPrototypes.PreparedRotation* c=caches) fixed(FixedVectorBits* p=points)
                {
                    spec.Caches=c;spec.Points=p;
                    foreach(int queries in new[]{1,8,32})
                    {
                        spec.Queries=queries;spec.Operation=QuaternionBenchOperation.RotateMany;ulong expected=Digest(data,spec);
                        foreach(var operation in new[]{QuaternionBenchOperation.RotateMany,QuaternionBenchOperation.RotateCached,QuaternionBenchOperation.RotateBuildCached})
                        {spec.Operation=operation;Batch(data,spec,"Q3","queries-per-quaternion="+queries+";exact-wide-coefficients",operation==QuaternionBenchOperation.RotateMany?0:(long)data.Length*sizeof(QuaternionBenchmarkPrototypes.PreparedRotation),expected);}
                    }
                }
            }
        }
        [Test]
        public void SeparateNegotiatedProfileMetadataAndCompactLimitRecomputation()
        {
            foreach(FixedMathProfile math in Enum.GetValues(typeof(FixedMathProfile)))
            {
                var spec=Spec(3,math);var data=QuaternionBenchmarkWorkload.Create(256,spec.Numeric);spec.CachedLimit=QuaternionBenchmarkWorkload.Limit(FixedMathKernel.Precision(spec.Numeric));
                foreach(var pair in new[]{new[]{QuaternionBenchOperation.Fingerprint,QuaternionBenchOperation.FingerprintCached},new[]{QuaternionBenchOperation.ValidateProfile,QuaternionBenchOperation.ValidateProfileCached},new[]{QuaternionBenchOperation.Limit,QuaternionBenchOperation.LimitCached}})
                {
                    spec.Operation=pair[0];ulong expected=Digest(data,spec);
                    foreach(var operation in pair){spec.Operation=operation;Batch(data,spec,"Q4","immutable negotiated profiles;limit guard="+FixedMathKernel.Precision(spec.Numeric),operation==pair[1]?operation==QuaternionBenchOperation.LimitCached?sizeof(FixedQ):data.Length*9L:0,expected);}
                }
            }
        }
        [Test]
        public void MeasureCodecBitCopyQuantizationDescriptorsAndFailedWrites()
        {
            byte* original=stackalloc byte[192];byte* candidate=stackalloc byte[192];
            foreach(int encoding in new[]{0,1,2,3,4,5}) foreach(bool lossy in encoding<3?new[]{false,true}:new[]{true})
            foreach(int offset in encoding>=2?new[]{0,5}:new[]{0})
            {
                var spec=Spec();ulong scale=spec.Numeric.Scale64;
                spec.Profile=encoding<3?new FixedQuaternionNetworkProfile(spec.Numeric.Settings,encoding==0?FixedCodec.FixedWidth:encoding==1?FixedCodec.VarInt:FixedCodec.BitPacked,lossy?2:-1,lossy,
                    lowerRaw:encoding==2?unchecked(0UL-(lossy?100UL:scale)):0,upperRaw:encoding==2?(lossy?100UL:scale):0):FixedQuaternionNetworkProfile.SmallestThree(spec.Numeric.Settings,encoding==3?32:encoding==4?48:64,true);
                spec.Offset=offset;var data=QuaternionBenchmarkWorkload.Create(32,spec.Numeric);
                // Guard/padding/atomicity checks cover every capacity and malformed/domain inputs too.
                foreach(var q in new[]{data[0].A,default,FixedQuaternionKernel.NaN,new FixedQuaternionBits(scale,scale,scale,scale),new FixedQuaternionBits(0x7ffffffffffffffeUL,0,0,scale)})
                for(int capacity=0;capacity<=40;capacity++) foreach(int prototype in new[]{0,1,2})
                {
                    for(int i=0;i<192;i++)original[i]=candidate[i]=0xa5;
                    var a=new FixedBufferWriter(original,capacity,offset);var b=new FixedBufferWriter(candidate,capacity,offset);
                    bool ok=FixedQuaternionNetworkKernel.Write(ref a,q,spec.Profile,spec.Numeric,out var status);
                    bool alternative=prototype==2?QuaternionBenchmarkPrototypes.ShortCompact(ref b,q,spec.Profile,spec.Numeric,out var s):QuaternionBenchmarkPrototypes.SingleStage(ref b,q,spec.Profile,spec.Numeric,prototype==1,out s);
                    Assert.AreEqual(ok,alternative);Assert.AreEqual(status,s);Assert.AreEqual(a.BitPosition,b.BitPosition);
                    for(int i=0;i<192;i++)Assert.AreEqual(original[i],candidate[i],"codec guard byte "+i);
                }
                var packets=new byte[data.Length*192];var lengths=new int[data.Length];
                fixed(byte* pin=packets) fixed(int* l=lengths)
                {
                    byte* packetData=pin;spec.Packets=packetData;spec.Lengths=l;
                    Action<bool> prepare=descriptor=>{for(int i=0;i<data.Length;i++){var writer=new FixedBufferWriter(packetData+i*192,192,descriptor?0:offset);bool ok=descriptor?FixedQuaternionNetworkKernel.WriteDescriptor(ref writer,spec.Profile,out var status):FixedQuaternionNetworkKernel.Write(ref writer,data[i].A,spec.Profile,spec.Numeric,out status);Assert.IsTrue(ok);lengths[i]=writer.BytesWritten;}};
                    prepare(false);spec.Operation=QuaternionBenchOperation.NetworkWrite;ulong expected=Digest(data,spec);
                    foreach(var operation in new[]{QuaternionBenchOperation.NetworkWrite,QuaternionBenchOperation.WriteStageControl,QuaternionBenchOperation.WriteByteCopy,QuaternionBenchOperation.NetworkRead,QuaternionBenchOperation.Canonicalize,QuaternionBenchOperation.ShortWrite,QuaternionBenchOperation.ShortWriteEarly})
                    {spec.Operation=operation;Batch(data,spec,"Q5","encoding="+encoding+";lossy="+lossy+";bit-offset="+offset,expected:operation==QuaternionBenchOperation.WriteStageControl||operation==QuaternionBenchOperation.WriteByteCopy?expected:operation==QuaternionBenchOperation.ShortWriteEarly?Digest(data,new QuaternionBenchmarkSpec {Numeric=spec.Numeric,Profile=spec.Profile,Offset=spec.Offset,Operation=QuaternionBenchOperation.ShortWrite}):(ulong?)null);}
                    if(offset==0){prepare(true);foreach(var operation in new[]{QuaternionBenchOperation.DescriptorWrite,QuaternionBenchOperation.DescriptorRead}){spec.Operation=operation;Batch(data,spec,"Q5","encoding="+encoding+";descriptor-negotiation");}}
                }
            }
        }
        [Test]
        public void RecordValueWideCoefficientAndScratchFootprints()
        {
            Assert.AreEqual(32,sizeof(FixedQuaternion));
            TestContext.Out.WriteLine($"QUATERNION_LAYOUT value={sizeof(FixedQuaternion)} raw={sizeof(FixedQuaternionBits)} wide={sizeof(FixedQ)} wideQuaternion={sizeof(FixedQuaternionQ)} profile={sizeof(FixedQuaternionNetworkProfile)} preparedRotation={sizeof(QuaternionBenchmarkPrototypes.PreparedRotation)} sample={sizeof(QuaternionBenchmarkSample)} codecScratch=40 maxDescriptor=172");
        }
    }
}

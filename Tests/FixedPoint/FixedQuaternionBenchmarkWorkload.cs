namespace LegendaryTools.FixedPoint.Tests.Benchmarks
{
    internal enum QuaternionBenchOperation
    {
        LoopControl, Normalize, Inverse, Rotate, Angle, Euler, EulerRead, Axis, AxisRead, FromTo, Look, Lerp, Slerp, RotateTowards,
        Ratio, RatioOnePass, RatioWord, RotateMany, RotateCached, RotateBuildCached,
        Fingerprint, FingerprintCached, ValidateProfile, ValidateProfileCached, Limit, LimitCached,
        NetworkWrite, WriteStageControl, WriteByteCopy, ShortWrite, ShortWriteEarly, NetworkRead, Canonicalize, DescriptorWrite, DescriptorRead
    }
    internal struct QuaternionBenchmarkSample
    {
        internal FixedQuaternionBits A,B;
        internal FixedVectorBits V,Up;
        internal ulong Amount, Fingerprint;
        internal FixedQ Numerator;
        internal FixedBigUInt Denominator;
        internal FixedQuaternionNetworkProfile Profile;
        internal bool ProfileValid;
    }
    internal unsafe struct QuaternionBenchmarkSpec
    {
        internal FixedRuntimeState Numeric;
        internal FixedQuaternionNetworkProfile Profile;
        internal QuaternionBenchOperation Operation;
        internal int Queries, Offset, PacketStride;
        internal FixedVectorBits* Points;
        internal QuaternionBenchmarkPrototypes.PreparedRotation* Caches;
        internal byte* Packets;
        internal int* Lengths;
        internal FixedQ CachedLimit;
    }
    internal struct QuaternionBenchmarkResult { internal ulong Digest; internal FixedStatus Status; internal long Bytes; internal int Managed; }
    internal static unsafe class QuaternionBenchmarkWorkload
    {
        internal static ulong Next(ref ulong value) { value ^= value << 13; value ^= value >> 7; value ^= value << 17; return value; }
        private static ulong Raw(ref ulong seed, ulong scale) => unchecked((ulong)((long)(Next(ref seed) % (scale * 4 + 1)) - (long)(scale * 2)));
        internal static QuaternionBenchmarkSample[] Create(int count, FixedRuntimeState state)
        {
            var values = new QuaternionBenchmarkSample[count]; ulong seed = 0xc44a36f185a7UL;
            for (int i = 0; i < count; i++)
            {
                ulong s = state.Scale64;
                var a = new FixedQuaternionBits(Raw(ref seed,s),Raw(ref seed,s),Raw(ref seed,s),s);
                var b = new FixedQuaternionBits(Raw(ref seed,s),Raw(ref seed,s),Raw(ref seed,s),s);
                a = FixedQuaternionKernel.Normalize(a,state,out _); b = FixedQuaternionKernel.Normalize(b,state,out _);
                var profile = i % 6 < 3 ? new FixedQuaternionNetworkProfile(state.Settings, i % 6 == 0 ? FixedCodec.FixedWidth : i % 6 == 1 ? FixedCodec.VarInt : FixedCodec.BitPacked,
                    lowerRaw: i % 6 == 2 ? unchecked(0UL-s) : 0, upperRaw: i % 6 == 2 ? s : 0) : FixedQuaternionNetworkProfile.SmallestThree(state.Settings,i%6==3?32:i%6==4?48:64,true);
                var n = FixedBigUInt.Multiply(FixedBigUInt.From(s),FixedBigUInt.From(s));
                var denominator = i % 2 == 0 ? FixedBigUInt.From(s) : n;
                n = FixedBigUInt.Multiply(n,FixedBigUInt.From((ulong)i+1));
                n = FixedBigUInt.Add(n,FixedBigUInt.From((ulong)(i%7+1)));
                values[i]=new QuaternionBenchmarkSample {A=a,B=b,V=new FixedVectorBits(Raw(ref seed,s),Raw(ref seed,s),Raw(ref seed,s)),Up=new FixedVectorBits(0,s,0),Amount=s/3,
                    Numerator=new FixedQ(n,(i&1)!=0),Denominator=denominator,Profile=profile,Fingerprint=profile.SchemaFingerprint,ProfileValid=profile.IsValid};
            }
            return values;
        }
        internal static FixedQ Limit(int p) => FixedQ.Divide(FixedQ.One(p),new FixedQ(FixedBigUInt.Sqrt(FixedQ.Integer(2,p).Magnitude,p)),p);
        private static ulong Hash(ulong hash, FixedQuaternionBits q) {for(int i=0;i<4;i++)hash=QuaternionBenchmarkFixture.Fold(hash,q.Get(i));return hash;}
        private static ulong Hash(ulong hash, FixedVectorBits v) {for(int i=0;i<3;i++)hash=QuaternionBenchmarkFixture.Fold(hash,v.Get(i));return hash;}
        private static ulong Hash(ulong hash, FixedQ value) {for(int i=0;i<16;i++)hash=QuaternionBenchmarkFixture.Fold(hash,value.Magnitude.Get(i));return QuaternionBenchmarkFixture.Fold(hash,value.Negative?1UL:0UL);}
        internal static void Run(QuaternionBenchmarkSample* data,int count,QuaternionBenchmarkSpec* spec,QuaternionBenchmarkResult* output)
        {
            var result=new QuaternionBenchmarkResult {Digest=14695981039346656037UL};
            byte* bytes=stackalloc byte[192]; var state=spec->Numeric;
            for(int i=0;i<count;i++)
            {
                ref var item=ref data[i]; FixedStatus status=FixedStatus.None; FixedQuaternionBits q=default; FixedVectorBits v=default;
                bool hasQuaternion=false,hasVector=false; ulong scalar=0;
                switch(spec->Operation)
                {
                    case QuaternionBenchOperation.LoopControl:q=item.A;hasQuaternion=true;break;
                    case QuaternionBenchOperation.Normalize:q=FixedQuaternionKernel.Normalize(item.A,state,out status);hasQuaternion=true;break;
                    case QuaternionBenchOperation.Inverse:q=FixedQuaternionKernel.Inverse(item.A,state,out status);hasQuaternion=true;break;
                    case QuaternionBenchOperation.Rotate:v=FixedQuaternionKernel.Rotate(item.A,item.V,state,out status);hasVector=true;break;
                    case QuaternionBenchOperation.Angle:scalar=FixedQuaternionKernel.Angle(item.A,item.B,false,state,out status);break;
                    case QuaternionBenchOperation.Euler:q=FixedQuaternionKernel.Euler(item.V,false,state,out status);hasQuaternion=true;break;
                    case QuaternionBenchOperation.EulerRead:v=FixedQuaternionKernel.EulerAngles(item.A,false,state,out status);hasVector=true;break;
                    case QuaternionBenchOperation.Axis:q=FixedQuaternionKernel.AngleAxis(item.Amount,item.V,false,state,out status);hasQuaternion=true;break;
                    case QuaternionBenchOperation.AxisRead:scalar=FixedQuaternionKernel.ToAngleAxis(item.A,false,state,out v,out status);hasVector=true;break;
                    case QuaternionBenchOperation.FromTo:q=FixedQuaternionKernel.FromTo(item.V,item.Up,state,out status);hasQuaternion=true;break;
                    case QuaternionBenchOperation.Look:q=FixedQuaternionKernel.LookRotation(item.V,item.Up,state,out status);hasQuaternion=true;break;
                    case QuaternionBenchOperation.Lerp:q=FixedQuaternionKernel.Interpolate(item.A,item.B,item.Amount,false,false,state,out status);hasQuaternion=true;break;
                    case QuaternionBenchOperation.Slerp:q=FixedQuaternionKernel.Interpolate(item.A,item.B,item.Amount,false,true,state,out status);hasQuaternion=true;break;
                    case QuaternionBenchOperation.RotateTowards:q=FixedQuaternionKernel.RotateTowards(item.A,item.B,item.Amount,false,state,out status);hasQuaternion=true;break;
                    case QuaternionBenchOperation.Ratio:scalar=FixedVectorKernel.RoundRatio(item.Numerator,item.Denominator,state,ref status);break;
                    case QuaternionBenchOperation.RatioOnePass:scalar=QuaternionBenchmarkPrototypes.Ratio(item.Numerator,item.Denominator,state,false,out status);break;
                    case QuaternionBenchOperation.RatioWord:scalar=QuaternionBenchmarkPrototypes.Ratio(item.Numerator,item.Denominator,state,true,out status);break;
                    case QuaternionBenchOperation.RotateMany:case QuaternionBenchOperation.RotateCached:case QuaternionBenchOperation.RotateBuildCached:
                        if(spec->Operation==QuaternionBenchOperation.RotateBuildCached) spec->Caches[i]=QuaternionBenchmarkPrototypes.Prepare(item.A,state);
                        for(int query=0;query<spec->Queries;query++)
                        {
                            var point=spec->Points[i*32+query];
                            v=spec->Operation==QuaternionBenchOperation.RotateMany ? FixedQuaternionKernel.Rotate(item.A,point,state,out status) : QuaternionBenchmarkPrototypes.Rotate(spec->Caches[i],point,state,out status);
                            result.Digest=Hash(result.Digest,v);result.Digest=QuaternionBenchmarkFixture.Fold(result.Digest,(uint)status);result.Status|=status;
                        }
                        break;
                    case QuaternionBenchOperation.Fingerprint:scalar=item.Profile.SchemaFingerprint;break;
                    case QuaternionBenchOperation.FingerprintCached:scalar=item.Fingerprint;break;
                    case QuaternionBenchOperation.ValidateProfile:scalar=item.Profile.IsValid?1UL:0UL;break;
                    case QuaternionBenchOperation.ValidateProfileCached:scalar=item.ProfileValid?1UL:0UL;break;
                    case QuaternionBenchOperation.Limit:result.Digest=Hash(result.Digest,Limit(FixedMathKernel.Precision(state)));break;
                    case QuaternionBenchOperation.LimitCached:result.Digest=Hash(result.Digest,spec->CachedLimit);break;
                    case QuaternionBenchOperation.NetworkWrite:case QuaternionBenchOperation.WriteStageControl:case QuaternionBenchOperation.WriteByteCopy:
                    case QuaternionBenchOperation.ShortWrite:case QuaternionBenchOperation.ShortWriteEarly:case QuaternionBenchOperation.DescriptorWrite:
                    {
                        for(int b=0;b<192;b++)bytes[b]=0xa5;
                        int capacity=spec->Operation==QuaternionBenchOperation.ShortWrite || spec->Operation==QuaternionBenchOperation.ShortWriteEarly ? 0 : 192;
                        var writer=new FixedBufferWriter(bytes,capacity,spec->Offset);bool ok;
                        if(spec->Operation==QuaternionBenchOperation.DescriptorWrite)ok=FixedQuaternionNetworkKernel.WriteDescriptor(ref writer,spec->Profile,out status);
                        else if(spec->Operation==QuaternionBenchOperation.WriteStageControl || spec->Operation==QuaternionBenchOperation.WriteByteCopy)ok=QuaternionBenchmarkPrototypes.SingleStage(ref writer,item.A,spec->Profile,state,spec->Operation==QuaternionBenchOperation.WriteByteCopy,out status);
                        else if(spec->Operation==QuaternionBenchOperation.ShortWriteEarly)ok=QuaternionBenchmarkPrototypes.ShortCompact(ref writer,item.A,spec->Profile,state,out status);
                        else ok=FixedQuaternionNetworkKernel.Write(ref writer,item.A,spec->Profile,state,out status);
                        scalar=ok?1UL:0UL; result.Bytes+=writer.BytesWritten;
                        result.Digest=QuaternionBenchmarkFixture.Fold(result.Digest,(ulong)writer.BitPosition);
                        // Include guards/padding, so prototypes cannot gain speed by changing atomicity.
                        for(int b=0;b<192;b++)result.Digest=QuaternionBenchmarkFixture.Fold(result.Digest,bytes[b]);
                        break;
                    }
                    case QuaternionBenchOperation.NetworkRead:case QuaternionBenchOperation.Canonicalize:case QuaternionBenchOperation.DescriptorRead:
                    {
                        byte* packet=spec->Packets+i*spec->PacketStride;int length=spec->Lengths[i];var reader=new FixedBufferReader(packet,length,spec->Offset);bool ok;
                        if(spec->Operation==QuaternionBenchOperation.DescriptorRead) {ok=FixedQuaternionNetworkKernel.ReadDescriptor(ref reader,out var p,out status);scalar=ok?p.SchemaFingerprint:0;}
                        else {ok=FixedQuaternionNetworkKernel.Read(ref reader,spec->Profile,state,out q,out status);hasQuaternion=true;scalar=ok?1UL:0UL;}
                        if(spec->Operation==QuaternionBenchOperation.Canonicalize)
                        {
                            var writer=new FixedBufferWriter(bytes,192,spec->Offset);ok=FixedQuaternionNetworkKernel.Write(ref writer,item.A,spec->Profile,state,out var writeStatus);status|=writeStatus;
                            reader=new FixedBufferReader(bytes,writer.BytesWritten,spec->Offset);ok&=FixedQuaternionNetworkKernel.Read(ref reader,spec->Profile,state,out q,out var readStatus);status|=readStatus;scalar=ok?1UL:0UL;
                        }
                        result.Bytes+=(reader.BitPosition+7)/8;result.Digest=QuaternionBenchmarkFixture.Fold(result.Digest,(ulong)reader.BitPosition);break;
                    }
                }
                if(hasQuaternion)result.Digest=Hash(result.Digest,q);if(hasVector)result.Digest=Hash(result.Digest,v);
                result.Digest=QuaternionBenchmarkFixture.Fold(result.Digest,scalar);result.Digest=QuaternionBenchmarkFixture.Fold(result.Digest,(uint)status);result.Status|=status;
            }
            MarkManaged(ref result.Managed);*output=result;
        }
#if !FIXEDPOINT_STANDALONE
        [Unity.Burst.BurstDiscard]
#endif
        private static void MarkManaged(ref int value) => value=1;
    }
}

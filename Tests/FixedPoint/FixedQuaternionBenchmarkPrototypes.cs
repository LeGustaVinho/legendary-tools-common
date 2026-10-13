namespace LegendaryTools.FixedPoint.Tests.Benchmarks
{
    /// <summary>Private experiments, deliberately excluded from the production library.</summary>
    internal static unsafe class QuaternionBenchmarkPrototypes
    {
        internal static FixedBigUInt DivRem(FixedBigUInt numerator, FixedBigUInt denominator, out FixedBigUInt rem, bool word)
        {
            if (word && denominator.BitLength <= 64)
            {
                var q = FixedBigUInt.Divide(numerator, denominator.Low, out ulong remainder);
                rem = FixedBigUInt.From(remainder); return q;
            }
            FixedBigUInt quotient = default; rem = default;
            for (int bit = numerator.BitLength - 1; bit >= 0; bit--)
            {
                bool carry = rem.Bit(511); rem = FixedBigUInt.ShiftLeft(rem, 1);
                if (numerator.Bit(bit)) rem.Set(0, rem.Get(0) | 1);
                if (carry || FixedBigUInt.Compare(rem, denominator) >= 0)
                { rem = FixedBigUInt.Subtract(rem, denominator); quotient.SetBit(bit); }
            }
            return quotient;
        }
        internal static ulong Ratio(FixedQ numerator, FixedBigUInt denominator, FixedRuntimeState state, bool word, out FixedStatus status)
        {
            status = FixedStatus.None;
            var q = DivRem(numerator.Magnitude, denominator, out var remainder, word);
            if (!remainder.IsZero)
            {
                status |= FixedStatus.Inexact; var rounding = state.Settings.Rounding;
                int half = FixedBigUInt.Compare(FixedBigUInt.ShiftLeft(remainder, 1), denominator);
                bool increment = rounding == FixedRounding.TowardPositiveInfinity && !numerator.Negative || rounding == FixedRounding.TowardNegativeInfinity && numerator.Negative ||
                    (rounding == FixedRounding.ToEven || rounding == FixedRounding.AwayFromZero) && (half > 0 || half == 0 && (rounding == FixedRounding.AwayFromZero || q.Bit(0)));
                if (increment) q = FixedBigUInt.Add(q, FixedBigUInt.From(1));
            }
            return FixedMathKernel.PackBig(q, numerator.Negative, new FixedFormat(FixedKind.Signed64, state), state.Settings, ref status);
        }
        internal struct PreparedRotation
        {
            internal FixedQ A0,A1,A2,B0,B1,B2,C0,C1,C2;
            internal FixedBigUInt Denominator;
            internal FixedStatus Status;
            internal FixedQ Get(int index) => index == 0 ? A0 : index == 1 ? A1 : index == 2 ? A2 : index == 3 ? B0 : index == 4 ? B1 : index == 5 ? B2 : index == 6 ? C0 : index == 7 ? C1 : C2;
            internal void Set(int index, FixedQ value) { switch(index) { case 0:A0=value;break;case 1:A1=value;break;case 2:A2=value;break;case 3:B0=value;break;case 4:B1=value;break;case 5:B2=value;break;case 6:C0=value;break;case 7:C1=value;break;default:C2=value;break; } }
        }
        private static FixedQ Raw(ulong bits, FixedRuntimeState state)
        { var value = new FixedFormat(FixedKind.Signed64, state).Decode(bits); return new FixedQ(FixedBigUInt.From(value.Magnitude), value.Negative); }
        private static FixedQ Product(FixedQ a, FixedQ b, FixedRuntimeState state) => new FixedQ(
            a.Magnitude.BitLength <= 64 && b.Magnitude.BitLength <= 64 ? FixedBigUInt.From(FixedUInt128.Multiply(a.Magnitude.Low,b.Magnitude.Low,state.Settings.ArithmeticMode)) : FixedBigUInt.Multiply(a.Magnitude,b.Magnitude), a.Negative ^ b.Negative);
        private static FixedQ Sum(FixedQ a, FixedQ b, FixedRuntimeState state)
        { var result=FixedQ.Add(a,b); if(result.IsZero && a.Negative!=b.Negative) result.Negative=state.Settings.Rounding==FixedRounding.TowardNegativeInfinity; return result; }
        internal static PreparedRotation Prepare(FixedQuaternionBits value, FixedRuntimeState state)
        {
            var cache=new PreparedRotation();
            if(!state.IsInitialized) {cache.Status=FixedStatus.Uninitialized;return cache;}
            if(!FixedQuaternionKernel.Finite(value,state)) {cache.Status=FixedStatus.InvalidDomain;return cache;}
            var q=new FixedQuaternionQ(Raw(value.X,state),Raw(value.Y,state),Raw(value.Z,state),Raw(value.W,state));
            if(q.IsZero) {cache.Status=FixedStatus.InvalidDomain;return cache;}
            var s2=new FixedQ(FixedBigUInt.From(FixedUInt128.Multiply(state.Scale64,state.Scale64,state.Settings.ArithmeticMode))); cache.Denominator=s2.Magnitude;
            for(int i=0;i<3;i++)
            {
                int j=(i+1)%3,k=(i+2)%3;
                cache.Set(i*3,Sum(s2,Sum(Product(q.Get(j),q.Get(j),state),Product(q.Get(k),q.Get(k),state),state).Shifted(1).Negated,state));
                cache.Set(i*3+1,Sum(Product(q.Get(i),q.Get(j),state),Product(q.Get(k),q.W,state).Negated,state).Shifted(1));
                cache.Set(i*3+2,Sum(Product(q.Get(i),q.Get(k),state),Product(q.Get(j),q.W,state),state).Shifted(1));
            }
            return cache;
        }
        internal static FixedVectorBits Rotate(in PreparedRotation cache, FixedVectorBits point, FixedRuntimeState state, out FixedStatus status)
        {
            status=cache.Status; if(!status.IsSuccess()) return FixedVectorKernel.NaN;
            var format=new FixedFormat(FixedKind.Signed64,state);
            for(int i=0;i<3;i++) if(!format.Decode(point.Get(i)).IsFinite) {status=FixedStatus.InvalidDomain;return FixedVectorKernel.NaN;}
            var v=new FixedQuaternionQ(Raw(point.X,state),Raw(point.Y,state),Raw(point.Z,state)); FixedVectorBits result=default;
            for(int i=0;i<3;i++)
            {
                int j=(i+1)%3,k=(i+2)%3;
                var numerator=Sum(Sum(Product(cache.Get(i*3),v.Get(i),state),Product(cache.Get(i*3+1),v.Get(j),state),state),Product(cache.Get(i*3+2),v.Get(k),state),state);
                result.Set(i,FixedVectorKernel.RoundRatio(numerator,cache.Denominator,state,ref status));
            }
            return result;
        }
        internal static bool SingleStage(ref FixedBufferWriter writer, FixedQuaternionBits value, FixedQuaternionNetworkProfile profile, FixedRuntimeState state, bool byteCopy, out FixedStatus status)
        {
            if(profile.Codec!=FixedQuaternionCodec.Components) return FixedQuaternionNetworkKernel.Write(ref writer,value,profile,state,out status);
            status=!state.IsInitialized ? FixedStatus.Uninitialized : !profile.IsValid ? FixedStatus.InvalidConfiguration : !profile.Settings.QuaternionNumericEquals(state.Settings) ? FixedStatus.ProfileMismatch : FixedStatus.None;
            if(!status.IsSuccess()) return false;
            if(profile.X.Codec!=FixedCodec.BitPacked && (writer.BitPosition&7)!=0) {status=FixedStatus.InvalidEncoding;return false;}
            byte* scratch=stackalloc byte[40]; for(int i=0;i<40;i++) scratch[i]=0;
            var stage=new FixedBufferWriter(scratch,40);
            for(int i=0;i<4;i++) {bool ok=FixedNetworkKernel.Write(ref stage,value.Get(i),profile.Get(i),state,out var s);status|=s;if(!ok)return false;}
            if(!writer.Has(stage.BitPosition)) {status|=FixedStatus.BufferTooSmall;return false;}
            if(byteCopy && (writer.BitPosition&7)==0 && (stage.BitPosition&7)==0)
            { for(int i=0;i<stage.BytesWritten;i++) writer.Buffer[(writer.BitPosition>>3)+i]=scratch[i]; writer.BitPosition+=stage.BitPosition; }
            else {var r=new FixedBufferReader(scratch,stage.BytesWritten);long left=stage.BitPosition;while(left>0){int bits=left>64?64:(int)left;writer.WriteBits(r.ReadBits(bits),bits);left-=bits;}}
            return true;
        }
        internal static bool ShortCompact(ref FixedBufferWriter writer, FixedQuaternionBits value, FixedQuaternionNetworkProfile profile, FixedRuntimeState state, out FixedStatus status)
        {
            if(profile.Codec==FixedQuaternionCodec.SmallestThree && state.IsInitialized && profile.IsValid && profile.Settings.QuaternionNumericEquals(state.Settings) && FixedQuaternionKernel.Finite(value,state))
            {
                var f=new FixedFormat(FixedKind.Signed64,state); bool nonzero=false;
                for(int i=0;i<4;i++) nonzero|=f.Decode(value.Get(i)).Magnitude!=0;
                // Compact integer codes fit Signed64 at every precision; successful encode always reports Inexact.
                if(nonzero && !writer.Has(profile.TotalBits)) {status=FixedStatus.BufferTooSmall|FixedStatus.Inexact;return false;}
            }
            return FixedQuaternionNetworkKernel.Write(ref writer,value,profile,state,out status);
        }
    }
}

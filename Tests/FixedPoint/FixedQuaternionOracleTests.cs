using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed class FixedQuaternionOracleTests
    {
        private static BigInteger Sqrt(BigInteger n)
        {
            if (n.IsZero) return 0; BigInteger x=n,y=(x+1)/2;
            while (y<x) {x=y;y=(x+n/x)/2;} return x;
        }
        private static long Root(BigInteger numerator,BigInteger denominator,bool negative,FixedRounding rounding,out bool inexact)
        {
            BigInteger r=Sqrt(numerator/denominator); inexact=r*r*denominator!=numerator;
            int comparison=(numerator*4).CompareTo(denominator*(2*r+1)*(2*r+1));
            bool increment=inexact && (rounding==FixedRounding.TowardPositiveInfinity && !negative || rounding==FixedRounding.TowardNegativeInfinity && negative ||
                (rounding==FixedRounding.ToEven || rounding==FixedRounding.AwayFromZero) && (comparison>0 || comparison==0 && (rounding==FixedRounding.AwayFromZero || !r.IsEven)));
            if (increment) r++;
            return r.IsZero && negative ? long.MaxValue-1 : (long)(negative ? -r : r);
        }
        [Test] public void FourDimensionalNormalizationMatchesExactIntegerMidpointBoundariesAcrossTheEntirePrecisionRange()
        {
            long[][] inputs={new[]{1L,1L,1L,1L},new[]{1L,-1L,0L,0L},new[]{long.MaxValue-2,long.MinValue+2,1L,-1L},new[]{1L,0L,0L,0L},new[]{3L,4L,5L,6L}};
            for (int precision=0;precision<=18;precision++)
            foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
            foreach (var input in inputs)
            {
                var state=new FixedRuntimeState(new FixedSettings(decimalPlaces64:precision,rounding:rounding)); BigInteger scale=BigInteger.Pow(10,precision), squares=0;
                foreach (long c in input) squares+=(BigInteger)c*c;
                var q=new FixedQuaternionBits(unchecked((ulong)input[0]),unchecked((ulong)input[1]),unchecked((ulong)input[2]),unchecked((ulong)input[3]));
                var actual=FixedQuaternionKernel.Normalize(q,state,out var status); var expected=new long[4]; FixedStatus expectedStatus=FixedStatus.None; bool allZero=true;
                for (int i=0;i<4;i++) {expected[i]=Root((BigInteger)input[i]*input[i]*scale*scale,squares,input[i]<0,rounding,out bool inexact); if(inexact) expectedStatus|=FixedStatus.Inexact; if(expected[i]!=0 && expected[i]!=long.MaxValue-1) allZero=false;}
                if (allZero) {Assert.AreEqual(FixedQuaternionKernel.NaN.X,actual.X); expectedStatus|=FixedStatus.InvalidDomain;}
                else for (int i=0;i<4;i++) Assert.AreEqual(unchecked((ulong)expected[i]),actual.Get(i),precision+"/"+rounding+"/component "+i);
                Assert.AreEqual(expectedStatus,status);
            }
        }
        [TestCase(FixedMathProfile.Accurate)] [TestCase(FixedMathProfile.Fast)]
        public void OneRawUnitAnglesRemainVisibleNearBothZeroAndPi(FixedMathProfile math)
        {
            foreach (var example in new[] {
                new long[] { 6, 3141591 }, new long[] { 9, 3141592652 }, new long[] { 18, 3141592653589793236 } })
            {
                var state=new FixedRuntimeState(new FixedSettings(decimalPlaces64:(int)example[0],mathProfile:math));
                var identity=FixedQuaternionKernel.Identity(state);
                var nearZero=new FixedQuaternionBits(0,1,0,state.Scale64);
                var nearPi=new FixedQuaternionBits(state.Scale64,0,0,1);
                // Independently rounded 2*atan(1/scale) and pi-2*atan(1/scale); atan must retain the tiny complement.
                Assert.AreEqual(2UL,FixedQuaternionKernel.Angle(identity,nearZero,false,state,out var smallStatus));
                Assert.AreEqual(unchecked((ulong)example[1]),FixedQuaternionKernel.Angle(identity,nearPi,false,state,out var largeStatus));
                Assert.IsTrue(smallStatus.IsSuccess()); Assert.IsTrue(largeStatus.IsSuccess());
            }
        }
        [Test] public void UninitializedAndNonFiniteGeometryNeverMutateCallerState()
        {
            var cold=default(FixedRuntimeState); var q=new FixedQuaternionBits(1,2,3,4); var v=new FixedVectorBits(1,2,3);
            Assert.AreEqual(FixedQuaternionKernel.NaN.X,FixedQuaternionKernel.Multiply(q,q,cold,out var status).X); Assert.AreEqual(FixedStatus.Uninitialized,status);
            FixedQuaternionKernel.Euler(v,false,cold,out status); Assert.AreEqual(FixedStatus.Uninitialized,status);
            FixedQuaternionKernel.LookRotation(v,v,cold,out status); Assert.AreEqual(FixedStatus.Uninitialized,status);
            var state=new FixedRuntimeState(FixedSettings.Default); var bad=FixedQuaternionKernel.NaN;
            FixedQuaternionKernel.Rotate(bad,v,state,out status); Assert.AreEqual(FixedStatus.InvalidDomain,status);
            FixedQuaternionKernel.Interpolate(q,bad,1,true,true,state,out status); Assert.AreEqual(FixedStatus.InvalidDomain,status);
        }
        [Test] public void GimbalSingularitiesChooseYZeroAndRoundTripTheOrientation()
        {
            FixedRuntime.Initialize();
            foreach (int x in new[]{90,-90})
            {
                var q=FixedQuaternion.Euler(x,35,67); var e=FixedQuaternion.ToEulerAngles(q,out var status); Assert.IsTrue(status.IsSuccess());
                // Quantized components can move an authored singularity slightly; the exactly representable equal-component case fixes the branch.
                var exact=new FixedQuaternion(x>0 ? Fixed64.FromRaw(707) : Fixed64.FromRaw(-707),0,0,Fixed64.FromRaw(707));
                var canonical=exact.eulerAngles; Assert.AreEqual(Fixed64.Zero,canonical.y);
                Assert.IsTrue(FixedQuaternion.ApproximatelyRotation(q,FixedQuaternion.Euler(e),Fixed64.FromRatio(1,5)));
            }
        }
    }
}

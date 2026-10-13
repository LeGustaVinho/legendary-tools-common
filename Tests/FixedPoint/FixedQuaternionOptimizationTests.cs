using System;
using System.Numerics;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedQuaternionOptimizationTests
    {
        private static BigInteger Integer(FixedBigUInt value) {BigInteger n=0;for(int i=15;i>=0;i--)n=(n<<32)+value.Get(i);return n;}
        private static ulong Next(ref ulong value) {value^=value<<13;value^=value>>7;value^=value<<17;return value;}
        [Test]
        public void ProductionDivRemMatchesIndependentBigIntegerAndLegacyAcrossAllLimbBoundaries()
        {
            ulong seed=0xca61b472UL;
            foreach(int nBits in new[]{0,1,31,63,64,65,127,128,255,256,511,512})
            foreach(int dBits in new[]{0,1,31,63,64,65,127,128,255,256,511,512})
            for(int sample=0;sample<4;sample++)
            {
                FixedBigUInt n=default,d=default;for(int i=0;i<16;i++){n.Set(i,(uint)Next(ref seed));d.Set(i,(uint)Next(ref seed));}
                n=FixedBigUInt.ShiftRight(n,512-nBits);d=FixedBigUInt.ShiftRight(d,512-dBits);
                if(nBits>0)n.SetBit(nBits-1);if(dBits>0)d.SetBit(dBits-1);
                var q=FixedBigUInt.DivRem(n,d,out var r);BigInteger expected=0,remainder=0;
                if(!d.IsZero)expected=BigInteger.DivRem(Integer(n),Integer(d),out remainder);
                Assert.AreEqual(expected,Integer(q));Assert.AreEqual(remainder,Integer(r));
                Assert.AreEqual(Integer(FixedBigUInt.Divide(n,d)),Integer(q));Assert.AreEqual(Integer(FixedBigUInt.Mod(n,d)),Integer(r));
            }
        }
        private static BigInteger Root(BigInteger n) {BigInteger x=n,y=(x+1)/2;while(y<x){x=y;y=(x+n/x)/2;}return x;}
        [TestCase(128)] [TestCase(256)]
        public void CompactLimitLiteralIsExactlyTheVersionOneIntegerExpression(int precision)
        {
            var actual=FixedQuaternionNetworkKernel.Limit(precision);
            var expected=(BigInteger.One<<(2*precision))/Root(BigInteger.One<<(2*precision+1));
            Assert.AreEqual(expected,Integer(actual.Magnitude));Assert.IsFalse(actual.Negative);
            var legacy=FixedQ.Divide(FixedQ.One(precision),new FixedQ(FixedBigUInt.Sqrt(FixedQ.Integer(2,precision).Magnitude,precision)),precision);
            Assert.AreEqual(Integer(legacy.Magnitude),Integer(actual.Magnitude));
        }
        [Test]
        public void CompactEarlyCapacityFailurePreservesFlagsAndDomainPrecedenceForEveryPrecision()
        {
            byte* bytes=stackalloc byte[8];
            for(int p=0;p<=18;p++)foreach(FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))foreach(FixedMathProfile math in Enum.GetValues(typeof(FixedMathProfile)))foreach(int bits in new[]{32,48,64})
            {
                var state=new FixedRuntimeState(new FixedSettings(decimalPlaces64:p,rounding:rounding,mathProfile:math));var profile=FixedQuaternionNetworkProfile.SmallestThree(state.Settings,bits,true);
                foreach(var q in new[]{FixedQuaternionKernel.Identity(state),new FixedQuaternionBits(1,1,1,1),new FixedQuaternionBits(0x7ffffffffffffffdUL,0x8000000000000002UL,1,1),default,FixedQuaternionKernel.NaN})
                {
                    for(int i=0;i<8;i++)bytes[i]=0xa5;var writer=new FixedBufferWriter(bytes,0,5);
                    Assert.IsFalse(FixedQuaternionNetworkKernel.Write(ref writer,q,profile,state,out var status));
                    bool invalid=!FixedQuaternionKernel.Finite(q,state)||q.X==0&&q.Y==0&&q.Z==0&&q.W==0;
                    Assert.AreEqual(invalid?FixedStatus.InvalidDomain:FixedStatus.BufferTooSmall|FixedStatus.Inexact,status);
                    Assert.AreEqual(5,writer.BitPosition);for(int i=0;i<8;i++)Assert.AreEqual(0xa5,bytes[i]);
                }
            }
        }
    }
}

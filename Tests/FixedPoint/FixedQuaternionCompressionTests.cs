using System;
using NUnit.Framework;

namespace LegendaryTools.FixedPoint.Tests
{
    public sealed unsafe class FixedQuaternionCompressionTests
    {
        [Test] public void RecordAngularErrorForEachPrecisionPresetAndRoundingPolicy()
        {
            byte* bytes=stackalloc byte[8];
            foreach (int precision in new[]{3,6,9,16,18})
            foreach (int bits in new[]{32,48,64})
            {
                double maximum=0;
                foreach (FixedRounding rounding in Enum.GetValues(typeof(FixedRounding)))
                {
                    var settings=new FixedSettings(decimalPlaces64:precision,rounding:rounding); var state=new FixedRuntimeState(settings); long s=(long)state.Scale64;
                    var profile=FixedQuaternionNetworkProfile.SmallestThree(settings,bits,true);
                    for (int sample=0;sample<96;sample++)
                    {
                        var angles=new FixedVectorBits(unchecked((ulong)(s*(sample%7-3)/2)),unchecked((ulong)(s*(sample%5-2)/2)),unchecked((ulong)(s*(sample%11-5)/3)));
                        var q=FixedQuaternionKernel.Euler(angles,false,state,out var status); Assert.IsTrue(status.IsSuccess());
                        var writer=new FixedBufferWriter(bytes,8); Assert.IsTrue(FixedQuaternionNetworkKernel.Write(ref writer,q,profile,state,out status));
                        var reader=new FixedBufferReader(bytes,writer.BytesWritten); Assert.IsTrue(FixedQuaternionNetworkKernel.Read(ref reader,profile,state,out var decoded,out status));
                        ulong raw=FixedQuaternionKernel.Angle(q,decoded,false,state,out status); Assert.IsTrue(status.IsSuccess());
                        double degrees=unchecked((long)raw)/(double)s*180/Math.PI; if(degrees>maximum) maximum=degrees;
                    }
                }
                TestContext.Progress.WriteLine("QUATERNION_ANGULAR_ERROR precision="+precision+" bits="+bits+" maxDegrees="+maximum.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                Assert.LessOrEqual(maximum,precision==3 ? 0.6 : bits==32 ? 0.6 : bits==48 ? 0.03 : 0.002);
            }
        }
    }
}

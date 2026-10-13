using System.Threading;

namespace LegendaryTools.Deterministic
{
    /// <summary>Host construction only. Local lifetime tokens are independent of network EntityIds/StateIds and never affect canonical bytes.</summary>
    public static class SpatialSourceIdentity
    {
        private static long next;
        public static ulong Create()
        {
            ulong token = unchecked((ulong)Interlocked.Increment(ref next));
            return token == 0 ? unchecked((ulong)Interlocked.Increment(ref next)) : token;
        }
    }
}

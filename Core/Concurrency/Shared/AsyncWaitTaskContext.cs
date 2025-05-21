using System;
using System.Threading;

namespace LegendaryTools.Threads
{
    public struct AsyncWaitTaskContext
    {
        public CancellationToken CancellationToken;
        public AsyncWaitBackend Backend;
        public IProgress<float> Progress;
    }
}
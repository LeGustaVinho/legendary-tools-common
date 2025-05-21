using System;
using System.Threading;

namespace LegendaryTools.Concurrency
{
    public struct AsyncWaitTaskContext
    {
        public CancellationToken CancellationToken;
        public AsyncWaitBackend Backend;
        public IProgress<float> Progress;
    }
}
using System;
using NUnit.Framework;

namespace LegendaryTools.Deterministic.Tests
{
    [Category("DeterministicRuntimeOptimization")]
    public sealed class LinkedReservedOptimizationTests
    {
        private sealed class Owner
        {
            internal readonly SlotMapNode<LinkedListValue<int>>[] Data = new SlotMapNode<LinkedListValue<int>>[8];
            internal readonly bool[] Prepared = new bool[8];
            internal int Failure = -1, Writes, Commits;
        }
        private struct Buffer : IDeterministicBuffer<SlotMapNode<LinkedListValue<int>>>
        {
            internal Owner Owner;
            public int Capacity => Owner.Data.Length;
            public ulong Revision => (ulong)Owner.Commits;
            public bool IsCreated => Owner != null;
            public SlotMapNode<LinkedListValue<int>> Read(int index) => Owner.Data[index];
            public void Write(int index, in SlotMapNode<LinkedListValue<int>> value)
            { if (!Owner.Prepared[index]) throw new InvalidOperationException("Unreserved write."); Owner.Data[index] = value; Owner.Writes++; }
            public DeterministicStatus CanPrepareWrite(int required, int start, int count)
                => Owner.Failure >= start && Owner.Failure < start + count ? DeterministicStatus.SnapshotCapacityExceeded : DeterministicStatus.None;
            public DeterministicStatus PrepareWrite(int required, int start, int count)
            { var status = CanPrepareWrite(required,start,count); if (status.IsSuccess()) for (int i=start;i<start+count;i++) Owner.Prepared[i]=true; return status; }
            public void CommitWrite() => Owner.Commits++;
        }
        [TestCase(false)] [TestCase(true)]
        public void EveryParticipatingSlotIsReservedBeforeInsertionOrRemoval(bool remove)
        {
            var owner = new Owner(); var buffer = new Buffer { Owner = owner }; var slots = default(SlotMapState); var links = default(LinkedListState);
            var handles = new StableHandle[5];
            for (int i = 0; i < handles.Length; i++) LinkedListKernel<int,Buffer>.TryAdd(ref buffer,ref slots,ref links,i,1,default,out handles[i]).ThrowIfFailure();
            LinkedListKernel<int,Buffer>.TryRemove(ref buffer,ref slots,ref links,handles[4]).ThrowIfFailure();
            var before = (SlotMapNode<LinkedListValue<int>>[])owner.Data.Clone(); var originalSlots = slots; var originalLinks = links;
            int writes = owner.Writes, commits = owner.Commits;
            foreach (int failure in remove ? new[] { 0, 1, 2, 4 } : new[] { 1, 2, 4 })
            {
                owner.Failure = failure; Array.Clear(owner.Prepared,0,owner.Prepared.Length);
                var status = remove ? LinkedListKernel<int,Buffer>.TryRemove(ref buffer,ref slots,ref links,handles[1])
                    : LinkedListKernel<int,Buffer>.TryAdd(ref buffer,ref slots,ref links,99,2,handles[2],out _);
                Assert.AreEqual(DeterministicStatus.SnapshotCapacityExceeded,status); Assert.AreEqual(originalSlots,slots); Assert.AreEqual(originalLinks,links);
                Assert.AreEqual(writes,owner.Writes); Assert.AreEqual(commits,owner.Commits); CollectionAssert.AreEqual(before,owner.Data);
            }
            owner.Failure = -1; Array.Clear(owner.Prepared,0,owner.Prepared.Length);
            if (remove)
            {
                LinkedListKernel<int,Buffer>.TryRemove(ref buffer,ref slots,ref links,handles[1]).ThrowIfFailure();
                LinkedListKernel<int,Buffer>.TryAdd(ref buffer,ref slots,ref links,99,1,default,out var reused).ThrowIfFailure();
                Assert.AreEqual(new StableHandle(4,2),reused); Assert.AreEqual(4,slots.Count);
            }
            else
            {
                LinkedListKernel<int,Buffer>.TryAdd(ref buffer,ref slots,ref links,99,2,handles[2],out var added).ThrowIfFailure();
                Assert.AreEqual(new StableHandle(4,2),added); Assert.AreEqual(handles[1],owner.Data[4].Value.Previous); Assert.AreEqual(handles[2],owner.Data[4].Value.Next);
            }
        }
        [Test]
        public void RetiredNodeKeepsNeighborsLinkedWithoutEnteringTheReuseQueue()
        {
            var owner = new Owner(); var buffer = new Buffer { Owner = owner }; var slots = default(SlotMapState); var links = default(LinkedListState);
            LinkedListKernel<int,Buffer>.TryAdd(ref buffer,ref slots,ref links,10,1,default,out var first).ThrowIfFailure();
            LinkedListKernel<int,Buffer>.TryAdd(ref buffer,ref slots,ref links,20,1,default,out var middle).ThrowIfFailure();
            LinkedListKernel<int,Buffer>.TryAdd(ref buffer,ref slots,ref links,30,1,default,out var last).ThrowIfFailure();
            var node=owner.Data[1]; node.Generation=uint.MaxValue; owner.Data[1]=node; middle=new StableHandle(1,uint.MaxValue);
            var head=owner.Data[0]; head.Value.Next=middle; owner.Data[0]=head; var tail=owner.Data[2]; tail.Value.Previous=middle; owner.Data[2]=tail;
            LinkedListKernel<int,Buffer>.TryRemove(ref buffer,ref slots,ref links,middle).ThrowIfFailure();
            Assert.AreEqual(last,owner.Data[0].Value.Next); Assert.AreEqual(first,owner.Data[2].Value.Previous); Assert.AreEqual(1,owner.Data[1].Retired);
            LinkedListKernel<int,Buffer>.TryAdd(ref buffer,ref slots,ref links,40,1,default,out var added).ThrowIfFailure(); Assert.AreEqual(new StableHandle(3,1),added);
        }
    }
}

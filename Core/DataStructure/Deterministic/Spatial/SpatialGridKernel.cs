namespace LegendaryTools.Deterministic
{
    internal static class SpatialGridKernel<TKey, TData, TBounds, TStorage>
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        where TStorage : struct, ISpatialStorage<TKey, TData, TBounds>
    {
        internal static bool Range(in SpatialRawBounds bounds, long size, int dimensions, int budget,
            out SpatialCellKey min, out SpatialCellKey max)
        {
            min = new SpatialCellKey(SpatialGeometry.Cell(bounds.MinX, size), SpatialGeometry.Cell(bounds.MinY, size), dimensions == 3 ? SpatialGeometry.Cell(bounds.MinZ, size) : 0);
            max = new SpatialCellKey(SpatialGeometry.Cell(bounds.MaxX, size), SpatialGeometry.Cell(bounds.MaxY, size), dimensions == 3 ? SpatialGeometry.Cell(bounds.MaxZ, size) : 0);
            ulong x = SpatialGeometry.Width(min.X, max.X), y = SpatialGeometry.Width(min.Y, max.Y), z = SpatialGeometry.Width(min.Z, max.Z);
            if (x >= (ulong)budget || y >= (ulong)budget || z >= (ulong)budget) return false;
            ulong xy = (x + 1) * (y + 1);
            return xy <= (ulong)budget && (z + 1) <= (ulong)budget / xy;
        }
        private static int Allocate(ref TStorage store)
        {
            if (store.State.FreeLink >= 0)
            { int i = store.State.FreeLink; store.State.FreeLink = store.ReadLink(i).NextFree; return i; }
            return store.State.LinkUsed++;
        }
        internal static void Add(ref TStorage store, int recordSlot, int dimensions)
        {
            var placement = store.ReadPlacement(recordSlot); placement.GridHead = -1;
            long size = SpatialGeometry.Normalize(unchecked((ulong)store.Options.CellSize.RawValue), LegendaryTools.FixedPoint.FixedRuntime.State);
            placement.Overflow = !Range(placement.Bounds, size, dimensions, store.Options.MaxCellsPerObject, out var min, out var max);
            if (placement.Overflow) store.State.OverflowCount++;
            if (!placement.Overflow)
            {
                for (long x = min.X; ; x++)
                {
                    for (long y = min.Y; ; y++)
                    {
                        for (long z = min.Z; ; z++)
                        {
                            var key = new SpatialCellKey(x, y, z); int cell = store.FindCell(key);
                            int head = cell < 0 ? -1 : store.ReadCell(cell).Value;
                            int link = Allocate(ref store);
                            store.WriteLink(link, new SpatialGridLink { Cell = key, RecordSlot = recordSlot, NextCell = head,
                                PreviousCell = -1, NextObject = placement.GridHead, NextFree = -1 });
                            if (head >= 0) { var old = store.ReadLink(head); old.PreviousCell = link; store.WriteLink(head, old); }
                            store.PutCell(key, link); placement.GridHead = link;
                            if (z == max.Z) break;
                        }
                        if (y == max.Y) break;
                    }
                    if (x == max.X) break;
                }
            }
            store.WritePlacement(recordSlot, placement);
        }
        internal static void Remove(ref TStorage store, int recordSlot)
        {
            var placement = store.ReadPlacement(recordSlot); int link = placement.GridHead;
            if (placement.Overflow) store.State.OverflowCount--;
            while (link >= 0)
            {
                var node = store.ReadLink(link); int next = node.NextObject;
                if (node.PreviousCell >= 0)
                { var previous = store.ReadLink(node.PreviousCell); previous.NextCell = node.NextCell; store.WriteLink(node.PreviousCell, previous); }
                else if (node.NextCell < 0) store.RemoveCell(node.Cell);
                else store.PutCell(node.Cell, node.NextCell);
                if (node.NextCell >= 0)
                { var following = store.ReadLink(node.NextCell); following.PreviousCell = node.PreviousCell; store.WriteLink(node.NextCell, following); }
                node.NextFree = store.State.FreeLink; store.WriteLink(link, node); store.State.FreeLink = link; link = next;
            }
            placement.GridHead = -1; placement.Overflow = false; store.WritePlacement(recordSlot, placement);
        }
        internal static void Rebuild(ref TStorage store, int dimensions)
        {
            store.ClearCells(); store.State.LinkUsed = 0; store.State.FreeLink = -1; store.State.OverflowCount = 0;
            for (int i = 0; i < store.State.Records.Used; i++) if (store.ReadEntry(i).Active != 0) Add(ref store, i, dimensions);
            store.State.Dirty = false;
        }
    }
}

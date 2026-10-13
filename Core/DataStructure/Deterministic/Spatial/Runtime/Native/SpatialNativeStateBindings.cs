#if !FIXEDPOINT_STANDALONE
namespace LegendaryTools.Deterministic
{
    public unsafe struct NativeSpatialStateAdapter<TKey, TData, TBounds, TBoundsAccess, TPolicy, TKeyCodec, TDataCodec, TBoundsCodec> : ICanonicalStateAdapter
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        where TBoundsAccess : unmanaged, ISpatialBoundsAccess<TBounds>
        where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
        where TKeyCodec : unmanaged, IDeterministicCodec<TKey>
        where TDataCodec : unmanaged, IDeterministicCodec<TData>
        where TBoundsCodec : unmanaged, IDeterministicCodec<TBounds>
    {
        private NativeDeterministicSpatialIndex<TKey, TData, TBounds, TBoundsAccess, TPolicy> index;
        private TKeyCodec keyCodec;
        private TDataCodec dataCodec;
        private TBoundsCodec boundsCodec;
        public NativeSpatialStateAdapter(NativeDeterministicSpatialIndex<TKey, TData, TBounds, TBoundsAccess, TPolicy> index,
            TKeyCodec keyCodec, TDataCodec dataCodec, TBoundsCodec boundsCodec)
        { this.index = index; this.keyCodec = keyCodec; this.dataCodec = dataCodec; this.boundsCodec = boundsCodec; }
        public DeterministicBackend Backend => DeterministicBackend.BurstCompiler;
        public DeterministicStatus TryWrite(ref DeterministicWriter writer) => index.TryWriteCanonical(ref writer, keyCodec, dataCodec, boundsCodec);
        public DeterministicStatus TryValidate(ref DeterministicReader reader, ref CanonicalScratch scratch) => index.TryValidateCanonical(ref reader, ref scratch, keyCodec, dataCodec, boundsCodec);
        public DeterministicStatus TryRead(ref DeterministicReader reader, ref CanonicalScratch scratch) => index.TryReadCanonical(ref reader, ref scratch, keyCodec, dataCodec, boundsCodec);
        internal DeterministicStatus TryReadDeferred(ref DeterministicReader reader, ref CanonicalScratch scratch) => index.TryReadCanonicalDeferred(ref reader, ref scratch, keyCodec, dataCodec, boundsCodec);
    }
    public static partial class SpatialStateBindings
    {
        public static DeterministicStateBinding Native<TKey, TData, TBounds, TBoundsAccess, TPolicy, TKeyCodec, TDataCodec, TBoundsCodec>(
            uint stateId, NativeDeterministicSpatialIndex<TKey, TData, TBounds, TBoundsAccess, TPolicy> index,
            TKeyCodec keyCodec, TDataCodec dataCodec, TBoundsCodec boundsCodec, ulong keyPolicyFingerprint, int maxCanonicalBytes, int scratchBytes)
            where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
            where TBoundsAccess : unmanaged, ISpatialBoundsAccess<TBounds>
            where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
            where TKeyCodec : unmanaged, IDeterministicCodec<TKey>
            where TDataCodec : unmanaged, IDeterministicCodec<TData>
            where TBoundsCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            if (!index.IsCreated || index.Options.Ownership != SpatialStateOwnership.Autonomous)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            ulong signature = Signature<TKey, TData, TBounds, TKeyCodec, TDataCodec, TBoundsCodec>(index.Dimensions, index.Options,
                keyCodec, dataCodec, boundsCodec, keyPolicyFingerprint);
            return new NativeSpatialRuntimeBinding<TKey, TData, TBounds, TBoundsAccess, TPolicy, TKeyCodec, TDataCodec, TBoundsCodec>(
                stateId, signature, maxCanonicalBytes, scratchBytes, index, keyCodec, dataCodec, boundsCodec);
        }
        public static DeterministicStateBinding Native2<TKey, TData, TPolicy, TKeyCodec, TDataCodec>(uint stateId,
            NativeDeterministicSpatialIndex2<TKey, TData, TPolicy> index, TKeyCodec keyCodec, TDataCodec dataCodec,
            ulong keyPolicyFingerprint, int maxCanonicalBytes, int scratchBytes)
            where TKey : unmanaged where TData : unmanaged
            where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
            where TKeyCodec : unmanaged, IDeterministicCodec<TKey> where TDataCodec : unmanaged, IDeterministicCodec<TData>
            => Native(stateId, index.Value, keyCodec, dataCodec, default(FixedBounds2Policy), keyPolicyFingerprint, maxCanonicalBytes, scratchBytes);
        public static DeterministicStateBinding Native3<TKey, TData, TPolicy, TKeyCodec, TDataCodec>(uint stateId,
            NativeDeterministicSpatialIndex3<TKey, TData, TPolicy> index, TKeyCodec keyCodec, TDataCodec dataCodec,
            ulong keyPolicyFingerprint, int maxCanonicalBytes, int scratchBytes)
            where TKey : unmanaged where TData : unmanaged
            where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
            where TKeyCodec : unmanaged, IDeterministicCodec<TKey> where TDataCodec : unmanaged, IDeterministicCodec<TData>
            => Native(stateId, index.Value, keyCodec, dataCodec, default(FixedBounds3Policy), keyPolicyFingerprint, maxCanonicalBytes, scratchBytes);
    }
}
#endif

namespace LegendaryTools.Deterministic
{
    public unsafe struct ManagedSpatialStateAdapter<TKey, TData, TBounds, TBoundsAccess, TPolicy, TKeyCodec, TDataCodec, TBoundsCodec> : ICanonicalStateAdapter
        where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
        where TBoundsAccess : unmanaged, ISpatialBoundsAccess<TBounds>
        where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
        where TKeyCodec : unmanaged, IDeterministicCodec<TKey>
        where TDataCodec : unmanaged, IDeterministicCodec<TData>
        where TBoundsCodec : unmanaged, IDeterministicCodec<TBounds>
    {
        private DeterministicSpatialIndex<TKey, TData, TBounds, TBoundsAccess, TPolicy> index;
        private TKeyCodec keyCodec;
        private TDataCodec dataCodec;
        private TBoundsCodec boundsCodec;
        public ManagedSpatialStateAdapter(DeterministicSpatialIndex<TKey, TData, TBounds, TBoundsAccess, TPolicy> index,
            TKeyCodec keyCodec, TDataCodec dataCodec, TBoundsCodec boundsCodec)
        { this.index = index; this.keyCodec = keyCodec; this.dataCodec = dataCodec; this.boundsCodec = boundsCodec; }
        public DeterministicBackend Backend => DeterministicBackend.Managed;
        public DeterministicStatus TryWrite(ref DeterministicWriter writer) => index.TryWriteCanonical(ref writer, keyCodec, dataCodec, boundsCodec);
        public DeterministicStatus TryValidate(ref DeterministicReader reader, ref CanonicalScratch scratch) => index.TryValidateCanonical(ref reader, ref scratch, keyCodec, dataCodec, boundsCodec);
        public DeterministicStatus TryRead(ref DeterministicReader reader, ref CanonicalScratch scratch) => index.TryReadCanonical(ref reader, ref scratch, keyCodec, dataCodec, boundsCodec);
        internal DeterministicStatus TryReadDeferred(ref DeterministicReader reader, ref CanonicalScratch scratch) => index.TryReadCanonicalDeferred(ref reader, ref scratch, keyCodec, dataCodec, boundsCodec);
    }
    public static partial class SpatialStateBindings
    {
        private static ulong Signature<TKey, TData, TBounds, TKeyCodec, TDataCodec, TBoundsCodec>(int dimensions, SpatialIndexOptions options,
            TKeyCodec keyCodec, TDataCodec dataCodec, TBoundsCodec boundsCodec, ulong keyPolicyFingerprint)
            where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
            where TKeyCodec : unmanaged, IDeterministicCodec<TKey>
            where TDataCodec : unmanaged, IDeterministicCodec<TData> where TBoundsCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            if (keyPolicyFingerprint == 0 || !FixedBoundsDeterministicProfile.TryFingerprint(out ulong numeric).IsSuccess()) return 0;
            uint payload = CanonicalFields.CombineSchema(0xd320, dataCodec.SchemaId, dataCodec.Version, boundsCodec.SchemaId, boundsCodec.Version);
            ulong semantics = DeterministicHash.Mix(options.SemanticFingerprint ^ keyPolicyFingerprint ^ numeric);
            // Configured codecs negotiate their full profile in addition to the fixed schema/version tags.
            if (keyCodec is ISpatialCodecFingerprint configuredKey)
            { if (configuredKey.CodecFingerprint == 0) return 0; semantics = DeterministicHash.Mix(semantics ^ configuredKey.CodecFingerprint); }
            if (boundsCodec is ISpatialCodecFingerprint configuredBounds)
            { if (configuredBounds.CodecFingerprint == 0) return 0; semantics = DeterministicHash.Mix(semantics ^ configuredBounds.CodecFingerprint); }
            if (dataCodec is ISpatialCodecFingerprint configuredData)
            { if (configuredData.CodecFingerprint == 0) return 0; semantics = DeterministicHash.Mix(semantics ^ configuredData.CodecFingerprint); }
            return StateBindingSignatures.Combine(dimensions == 2 ? DeterministicCollectionKind.SpatialIndex2 : DeterministicCollectionKind.SpatialIndex3,
                semantics, keyCodec.SchemaId, keyCodec.Version, payload, 1);
        }
        public static DeterministicStateBinding Managed<TKey, TData, TBounds, TBoundsAccess, TPolicy, TKeyCodec, TDataCodec, TBoundsCodec>(
            uint stateId, DeterministicSpatialIndex<TKey, TData, TBounds, TBoundsAccess, TPolicy> index,
            TKeyCodec keyCodec, TDataCodec dataCodec, TBoundsCodec boundsCodec, ulong keyPolicyFingerprint, int maxCanonicalBytes, int scratchBytes)
            where TKey : unmanaged where TData : unmanaged where TBounds : unmanaged
            where TBoundsAccess : unmanaged, ISpatialBoundsAccess<TBounds>
            where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
            where TKeyCodec : unmanaged, IDeterministicCodec<TKey>
            where TDataCodec : unmanaged, IDeterministicCodec<TData>
            where TBoundsCodec : unmanaged, IDeterministicCodec<TBounds>
        {
            if (index == null || !index.IsCreated || index.Options.Ownership != SpatialStateOwnership.Autonomous)
                DeterministicStatus.InvalidConfiguration.ThrowIfFailure();
            ulong signature = Signature<TKey, TData, TBounds, TKeyCodec, TDataCodec, TBoundsCodec>(index.Dimensions, index.Options,
                keyCodec, dataCodec, boundsCodec, keyPolicyFingerprint);
            return new ManagedSpatialRuntimeBinding<TKey, TData, TBounds, TBoundsAccess, TPolicy, TKeyCodec, TDataCodec, TBoundsCodec>(
                stateId, signature, maxCanonicalBytes, scratchBytes, index, keyCodec, dataCodec, boundsCodec);
        }
        public static DeterministicStateBinding Managed2<TKey, TData, TPolicy, TKeyCodec, TDataCodec>(uint stateId,
            DeterministicSpatialIndex2<TKey, TData, TPolicy> index, TKeyCodec keyCodec, TDataCodec dataCodec,
            ulong keyPolicyFingerprint, int maxCanonicalBytes, int scratchBytes)
            where TKey : unmanaged where TData : unmanaged
            where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
            where TKeyCodec : unmanaged, IDeterministicCodec<TKey> where TDataCodec : unmanaged, IDeterministicCodec<TData>
            => Managed(stateId, index, keyCodec, dataCodec, default(FixedBounds2Policy), keyPolicyFingerprint, maxCanonicalBytes, scratchBytes);
        public static DeterministicStateBinding Managed3<TKey, TData, TPolicy, TKeyCodec, TDataCodec>(uint stateId,
            DeterministicSpatialIndex3<TKey, TData, TPolicy> index, TKeyCodec keyCodec, TDataCodec dataCodec,
            ulong keyPolicyFingerprint, int maxCanonicalBytes, int scratchBytes)
            where TKey : unmanaged where TData : unmanaged
            where TPolicy : unmanaged, IDeterministicEquality<TKey>, IDeterministicComparer<TKey>
            where TKeyCodec : unmanaged, IDeterministicCodec<TKey> where TDataCodec : unmanaged, IDeterministicCodec<TData>
            => Managed(stateId, index, keyCodec, dataCodec, default(FixedBounds3Policy), keyPolicyFingerprint, maxCanonicalBytes, scratchBytes);
    }
}

namespace LegendaryTools.Deterministic
{
    public static class StateBindingSignatures
    {
        /// <summary>semanticFingerprint must cover capacity/growth rules, key policies and application codec options.</summary>
        public static ulong Combine(DeterministicCollectionKind kind, ulong semanticFingerprint,
            uint schemaId, ushort version, uint secondSchemaId = 0, ushort secondVersion = 0)
        {
            if (semanticFingerprint == 0 || schemaId == 0 || version == 0 ||
                (secondSchemaId == 0) != (secondVersion == 0)) return 0;
            ulong hash = DeterministicHash.Mix(0x4c445342494e4431UL ^ (byte)kind);
            hash = DeterministicHash.Mix(hash ^ semanticFingerprint);
            hash = DeterministicHash.Mix(hash ^ ((ulong)schemaId << 16 | version));
            return DeterministicHash.Mix(hash ^ ((ulong)secondSchemaId << 16 | secondVersion));
        }
    }
}

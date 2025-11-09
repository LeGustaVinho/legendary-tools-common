namespace LegendaryTools.SOAP.Variables.Scopes
{
    /// <summary>
    /// Scope discriminator used by ScopedReference to report active resolution.
    /// </summary>
    public enum VariableScope
    {
        Global = 0, // Constant or SOVariable (default)
        Session = 1, // Runtime-only override (per instance)
        Scene = 2, // Scene instance override (serialized in scene)
        Prefab = 3 // Prefab asset default override (serialized in prefab)
    }
}
namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Defines which Unity update loop will be used to poll/read from the UI side (FromUI / TwoWay).
    /// </summary>
    public enum UpdatePhase
    {
        Update,
        LateUpdate,
        FixedUpdate
    }
}
namespace LegendaryTools.ViewBinding
{
    public enum MissingEndpointPolicy
    {
        Wait = 0,
        Disable = 1,
        ClearTarget = 2,
        UseFallback = 3,
        ReResolve = 4,
        ReportError = 5
    }
}

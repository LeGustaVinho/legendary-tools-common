namespace LegendaryTools.ViewBinding
{
    public interface IBindingEndpointAvailabilityBackend
    {
        BindingEndpointAvailability GetEndpointAvailability(
            BindingEndpoint endpoint,
            out string error);
    }
}

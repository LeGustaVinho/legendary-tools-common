using System;

namespace LegendaryTools.ViewBinding
{
    internal interface IBindingEndpointIdentityBackend
    {
        bool TryGetEndpointIdentity(
            BindingEndpoint endpoint,
            out object identity,
            out Type resolvedType,
            out bool isStatic,
            out string error);
    }
}

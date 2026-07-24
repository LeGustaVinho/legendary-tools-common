using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public sealed class SingleSourceBindingSourceBackend : IBindingSourceBackend
    {
        public bool TryGetMetadata(
            IReadOnlyList<BindingSource> sources,
            out BindingMemberMetadata metadata,
            out string error)
        {
            if (!TryGetEndpoint(sources, out BindingEndpoint endpoint, out error))
            {
                metadata = default;
                return false;
            }

            return BindingBackendRegistry.MemberBackend.TryGetMetadata(endpoint, out metadata, out error);
        }

        public bool TryRead(IReadOnlyList<BindingSource> sources, out object value, out string error)
        {
            if (!TryGetEndpoint(sources, out BindingEndpoint endpoint, out error))
            {
                value = null;
                return false;
            }

            return BindingBackendRegistry.MemberBackend.TryRead(endpoint, out value, out error);
        }

        public bool TryWrite(IReadOnlyList<BindingSource> sources, object value, out string error)
        {
            if (!TryGetEndpoint(sources, out BindingEndpoint endpoint, out error))
            {
                return false;
            }

            return BindingBackendRegistry.MemberBackend.TryWrite(endpoint, value, out error);
        }

        private static bool TryGetEndpoint(
            IReadOnlyList<BindingSource> sources,
            out BindingEndpoint endpoint,
            out string error)
        {
            if (sources == null || sources.Count != 1 || sources[0] == null)
            {
                endpoint = null;
                error = "The default source backend requires exactly one source. Replace BindingBackendRegistry.SourceBackend to support source composition.";
                return false;
            }

            endpoint = sources[0].Endpoint;
            error = string.Empty;
            return true;
        }
    }
}

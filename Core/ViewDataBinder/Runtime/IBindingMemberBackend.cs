using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public interface IBindingMemberBackend
    {
        IReadOnlyList<BindingMemberDescriptor> GetMembers(BindingInstanceHandle root, int maxDepth);

        bool TryGetMetadata(
            BindingInstanceHandle root,
            string memberPath,
            out BindingMemberMetadata metadata,
            out string error);

        bool TryGetMetadata(BindingEndpoint endpoint, out BindingMemberMetadata metadata, out string error);

        bool TryRead(BindingEndpoint endpoint, out object value, out string error);

        bool TryWrite(BindingEndpoint endpoint, object value, out string error);
    }
}

using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public interface IBindingMemberSearchBackend
    {
        IReadOnlyList<BindingMemberDescriptor> SearchMembers(
            BindingInstanceHandle root,
            int maxDepth,
            string query,
            int maxResults);
    }
}

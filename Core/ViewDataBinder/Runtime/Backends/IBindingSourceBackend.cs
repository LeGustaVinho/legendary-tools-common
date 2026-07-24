using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public interface IBindingSourceBackend
    {
        bool TryGetMetadata(IReadOnlyList<BindingSource> sources, out BindingMemberMetadata metadata, out string error);

        bool TryRead(IReadOnlyList<BindingSource> sources, out object value, out string error);

        bool TryWrite(IReadOnlyList<BindingSource> sources, object value, out string error);
    }
}

namespace LegendaryTools.ViewBinding
{
    public interface IBindingInstanceResolver
    {
        bool TryResolve(BindingInstanceReference reference, out BindingInstanceHandle handle, out string error);
    }
}

namespace LegendaryTools.ViewBinding.Editor
{
    public interface IViewDataBindingInspectorExtension
    {
        BindingInspectorExtensionPlacement Placement { get; }

        int Order { get; }

        void Draw(BindingInspectorContext context);
    }
}

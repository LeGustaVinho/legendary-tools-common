#if UNITY_EDITOR
using LegendaryTools.NodeEditor;

namespace LegendaryTools.Maestro.NodeEditor
{
    /// <summary>
    /// Maestro-specific menu provider. Inherits generic node menu/toolbar behavior and
    /// preserves edge menu behavior through the default edge provider.
    /// Override methods if you want Maestro-specific menu paths/labels.
    /// </summary>
    public sealed class MaestroNodeEditorMenu :
        DefaultGraphMenu<InitStepNodeEditor, InitStepConfig>
    {
        public MaestroNodeEditorMenu() : base(false)
        {
        }
    }
}
#endif
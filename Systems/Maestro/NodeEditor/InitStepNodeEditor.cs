using LegendaryTools.Inspector;
using LegendaryTools.NodeEditor;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LegendaryTools.Maestro.NodeEditor
{
    public class InitStepNodeEditor : Node
    {
        [InlineEditor] public InitStepConfig InitStepConfig;
    }
}
// Assets/legendary-tools-common/Editor/NodeEditor/Runtime/IHasConfig.cs

using UnityEngine;

namespace LegendaryTools.NodeEditor
{
    /// <summary>
    /// Contract used by DefaultGraph<TNode, TConfig> so it can set/read a ScriptableObject config on nodes generically.
    /// </summary>
    public interface IHasConfig<TConfig> where TConfig : ScriptableObject
    {
        /// <summary>Gets or sets the ScriptableObject config attached to this node.</summary>
        TConfig Config { get; set; }
    }
}
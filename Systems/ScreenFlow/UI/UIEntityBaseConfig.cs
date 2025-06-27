using LegendaryTools.Systems.AssetProvider;
using UnityEngine;
using UnityEngine.Serialization;

namespace LegendaryTools.Systems.ScreenFlow
{
    public abstract class UIEntityBaseConfig : ScriptableObject
    {
        public AssetLoaderConfig AssetLoaderConfig;
        
        public TransitionMode TransitionMode;
    }
}
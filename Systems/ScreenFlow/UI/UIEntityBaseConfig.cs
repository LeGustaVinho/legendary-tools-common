using LegendaryTools.Systems.AssetProvider;
using UnityEngine;

namespace LegendaryTools.Systems.ScreenFlow
{
    public abstract class UIEntityBaseConfig : ScriptableObject
    {
        public AssetLoaderConfig AssetLoaderConfig;
        
        public AnimationType AnimationType = AnimationType.NoAnimation;
    }
}
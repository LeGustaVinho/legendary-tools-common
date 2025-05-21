using LegendaryTools.Systems.AssetProvider;
using UnityEngine;

namespace LegendaryTools.Bragi
{
    public abstract class AudioConfigBase : ScriptableObject
    {
        public AssetLoaderConfig AssetLoadableConfig;
        
        public abstract AudioHandler[] Play(AudioSettings overrideSettings = null, bool allowFading = true);

        public abstract AudioHandler[] Play(Vector3 position, AudioSettings overrideSettings = null, bool allowFading = true);

        public abstract AudioHandler[] Play(Transform parent, AudioSettings overrideSettings = null, bool allowFading = true);
    }
}
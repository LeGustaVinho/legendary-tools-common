using System;
using System.Collections.Generic;
using LegendaryTools.Systems.AssetProvider;
using UnityEngine;

namespace LegendaryTools.Actor
{
#if !ODIN_INSPECTOR
    [Serializable]
#endif
    public class TypeOfActorAssetLoader
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.TypeDrawerSettings(BaseType = typeof(Actor),
            Filter = Sirenix.OdinInspector.TypeInclusionFilter.IncludeConcreteTypes | Sirenix.OdinInspector.TypeInclusionFilter.IncludeGenerics)]
        public Type SerializableType;
#else
        [TypeFilter(typeof(Actor))]
        public SerializableType SerializableType;
#endif
        public AssetLoaderConfig AssetLoaderConfig;
    }

    [CreateAssetMenu(menuName = "Tools/ActorSystem/ActorSystemAssetLoadableConfig",
        fileName = "ActorSystemAssetLoadableConfig", order = 0)]
    public class ActorSystemAssetLoadableConfig :
#if ODIN_INSPECTOR
        Sirenix.OdinInspector.SerializedScriptableObject
#else
        UnityEngine.ScriptableObject
#endif
    {
        public List<TypeOfActorAssetLoader> TypeByActorAssetLoaders =
            new List<TypeOfActorAssetLoader>();
        
        [HideInInspector]
        public Dictionary<Type, AssetLoaderConfig> TypeByActorAssetLoadersTable =
            new Dictionary<Type, AssetLoaderConfig>();

        public void Initialize()
        {
            TypeByActorAssetLoadersTable.Clear();
            foreach (TypeOfActorAssetLoader typeByActorAssetLoader in TypeByActorAssetLoaders)
            {
#if ODIN_INSPECTOR
                Type currentType = typeByActorAssetLoader.SerializableType;
#else
                Type currentType = typeByActorAssetLoader.SerializableType.Type;
#endif
                
                if (!TypeByActorAssetLoadersTable.TryAdd(currentType,
                        typeByActorAssetLoader.AssetLoaderConfig))
                {
                    Debug.LogError(
                        $"[ActorSystemAssetLoadableConfig:Initialize] Type {currentType} already exists in ActorSystemAssetLoadableConfig");
                }
            }
        }
    }
}
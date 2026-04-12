using System;

namespace LegendaryTools.Editor
{
    [Flags]
    internal enum ReferenceTrackerSearchScope
    {
        None = 0,
        CurrentScene = 1 << 0,
        ScenesInProject = 1 << 1,
        PrefabMode = 1 << 2,
        Prefabs = 1 << 3,
        Materials = 1 << 4,
        ScriptableObjects = 1 << 5,
        Others = 1 << 6,
        AnimatorControllersAndAnimationClips = 1 << 7,
        TimelineAssets = 1 << 8,
        AddressablesGroups = 1 << 9,
        ResourcesFolders = 1 << 10,
        AssetBundles = 1 << 11,
    }
}

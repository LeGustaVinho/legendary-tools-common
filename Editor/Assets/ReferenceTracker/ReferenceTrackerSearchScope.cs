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
    }
}

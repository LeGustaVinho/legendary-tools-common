using System;

namespace LegendaryTools.Editor
{
    [Flags]
    internal enum ReferenceTrackerSearchScope
    {
        None = 0,
        CurrentScene = 1,
        PrefabMode = 2,
    }
}

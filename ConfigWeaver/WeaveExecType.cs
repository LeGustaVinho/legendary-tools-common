using System;

namespace LegendaryTools
{
    [Flags]
    public enum WeaveExecType
    {
        EnteredEditMode,
        ExitingEditMode,
        AfterCompile,
    }
}
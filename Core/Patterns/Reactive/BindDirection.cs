using System;

namespace LegendaryTools.Reactive
{
    /// <summary>
    /// Indicates how data flows between Observable and UI.
    /// </summary>
    public enum BindDirection
    {
        TwoWay = 0,
        ToUI = 1,
        FromUI = 2
    }
}
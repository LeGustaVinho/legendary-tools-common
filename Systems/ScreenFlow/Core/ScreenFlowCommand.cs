using System;
using Object = UnityEngine.Object;

namespace LegendaryTools.Systems.ScreenFlow
{
    public readonly struct ScreenFlowCommand
    {
        public readonly ScreenFlowCommandType Type;
        public readonly System.Object Object;
        public readonly System.Object Args;
        public readonly Action<IScreenBase> OnShow;
        public readonly Action<IScreenBase> OnHide;

        public ScreenFlowCommand(ScreenFlowCommandType type, System.Object o, object args, Action<IScreenBase> onShow = null, Action<IScreenBase> onHide = null)
        {
            Type = type;
            Object = o;
            Args = args;
            OnShow = onShow;
            OnHide = onHide;
        }
    }
}
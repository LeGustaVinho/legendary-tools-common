using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LegendaryTools.Systems.ScreenFlow
{
    public class CommandQueueProcessor
    {
        private readonly List<ScreenFlowCommand> commandQueue = new List<ScreenFlowCommand>();
        private readonly ScreenFlow screenFlow;
        private readonly PopupManager popupManager;
        private readonly UIEntityLoader uiEntityLoader;
        private Task transitionRoutine;

        public bool IsTransiting => transitionRoutine != null;

        public CommandQueueProcessor(ScreenFlow screenFlow, PopupManager popupManager, UIEntityLoader uiEntityLoader)
        {
            this.screenFlow = screenFlow;
            this.popupManager = popupManager;
            this.uiEntityLoader = uiEntityLoader;
        }

        public async Task ProcessCommand(ScreenFlowCommand command)
        {
            if (IsTransiting)
            {
                commandQueue.Add(command);
                while (!command.IsDone)
                {
                    await Task.Yield();
                }
            }
            else
            {
                commandQueue.Add(command);
                transitionRoutine = ProcessCommandQueue();
                while (!command.IsDone)
                {
                    await Task.Yield();
                }
                
                if(commandQueue.Count > 0)
                    WaitCommandQueue().FireAndForget();
                else
                    transitionRoutine = null;
                
            }
        }

        private async Task WaitCommandQueue()
        {
            if (transitionRoutine != null)
            {
                await transitionRoutine;
                transitionRoutine = null;
            }
        }
        
        public void Dispose()
        {
            commandQueue.Clear();
            transitionRoutine = null;
        }

        private async Task ProcessCommandQueue()
        {
            while (commandQueue.Count > 0)
            {
                ScreenFlowCommand next = commandQueue[0];
                commandQueue.RemoveAt(0);

                switch (next.Type)
                {
                    case ScreenFlowCommandType.Trigger:
                        switch (next.Object)
                        {
                            case ScreenConfig screenConfig:
                                await screenFlow.ScreenTransitTo(screenConfig, false, next.Args, next.OnShow, next.OnHide);
                                next.IsDone = true;
                                break;
                            case PopupConfig popupConfig:
                                if (screenFlow.CurrentScreenConfig != null &&
                                    screenFlow.CurrentScreenConfig.AllowPopups)
                                {
                                    await popupManager.PopupTransitTo(popupConfig, screenFlow.CurrentScreenConfig,
                                        next.Args, next.OnShow, next.OnHide, screenFlow);
                                    next.IsDone = true;
                                }

                                break;
                        }
                        break;
                    case ScreenFlowCommandType.MoveBack:
                        await MoveBackOp(next.Args, next.OnShow, next.OnHide);
                        next.IsDone = true;
                        break;
                    case ScreenFlowCommandType.ClosePopup:
                        await popupManager.ClosePopupOp(next.Object as IPopupBase, next.Args, next.OnShow, next.OnHide, screenFlow);
                        next.IsDone = true;
                        break;
                }
            }
        }

        private async Task MoveBackOp(object args, Action<IScreenBase> onShow, Action<IScreenBase> onHide)
        {
            EntityArgPair<ScreenConfig> previousScreenConfig = screenFlow.ScreensHistory.Count > 1 ? screenFlow.ScreensHistory[screenFlow.ScreensHistory.Count - 2] : null;
            if (previousScreenConfig != null)
            {
                if (screenFlow.CurrentScreenConfig.CanMoveBackFromHere && previousScreenConfig.Entity.CanMoveBackToHere)
                {
                    await screenFlow.ScreenTransitTo(previousScreenConfig.Entity, true, args ?? previousScreenConfig.Args, onShow, onHide);
                }
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Systems.ScreenFlow
{
    public class CommandQueueProcessor
    {
        private readonly List<ScreenFlowCommand> commandQueue = new();
        private readonly ScreenFlow screenFlow;
        private readonly PopupManager popupManager;
        private readonly UIEntityLoader uiEntityLoader;
        private Task transitionRoutine;

        public bool IsTransiting => transitionRoutine != null;

        public CommandQueueProcessor(ScreenFlow screenFlow, PopupManager popupManager, UIEntityLoader uiEntityLoader)
        {
            this.screenFlow = screenFlow ?? throw new ArgumentNullException(nameof(screenFlow));
            this.popupManager = popupManager ?? throw new ArgumentNullException(nameof(popupManager));
            this.uiEntityLoader = uiEntityLoader ?? throw new ArgumentNullException(nameof(uiEntityLoader));
            if (screenFlow.Verbose)
                Debug.Log("[CommandQueueProcessor:Constructor] -> CommandQueueProcessor initialized");
        }

        public async Task ProcessCommand(ScreenFlowCommand command)
        {
            if (command == null)
            {
                Debug.LogError("[CommandQueueProcessor:ProcessCommand] -> Command is null");
                return;
            }

            if (IsTransiting)
            {
                commandQueue.Add(command);
                if (screenFlow.Verbose)
                    Debug.Log(
                        $"[CommandQueueProcessor:ProcessCommand] -> Queued command {command.Type} as transition is in progress");
                while (!command.IsDone)
                {
                    await Task.Yield();
                }
            }
            else
            {
                commandQueue.Add(command);
                if (screenFlow.Verbose)
                    Debug.Log($"[CommandQueueProcessor:ProcessCommand] -> Processing command {command.Type}");
                transitionRoutine = ProcessCommandQueue();
                while (!command.IsDone)
                {
                    await Task.Yield();
                }

                if (commandQueue.Count > 0)
                {
                    if (screenFlow.Verbose)
                        Debug.Log("[CommandQueueProcessor:ProcessCommand] -> Additional commands in queue, processing");
                    WaitCommandQueue().FireAndForget();
                }
                else
                {
                    transitionRoutine = null;
                    if (screenFlow.Verbose)
                        Debug.Log("[CommandQueueProcessor:ProcessCommand] -> No more commands in queue");
                }
            }
        }

        private async Task WaitCommandQueue()
        {
            if (transitionRoutine != null)
            {
                await transitionRoutine;
                transitionRoutine = null;
                if (screenFlow.Verbose)
                    Debug.Log("[CommandQueueProcessor:WaitCommandQueue] -> Completed waiting for command queue");
            }
        }

        public void Dispose()
        {
            commandQueue.Clear();
            transitionRoutine = null;
            if (screenFlow.Verbose) Debug.Log("[CommandQueueProcessor:Dispose] -> CommandQueueProcessor disposed");
        }

        private async Task ProcessCommandQueue()
        {
            while (commandQueue.Count > 0)
            {
                ScreenFlowCommand next = commandQueue[0];
                commandQueue.RemoveAt(0);
                if (screenFlow.Verbose)
                    Debug.Log($"[CommandQueueProcessor:ProcessCommandQueue] -> Processing command: {next.Type}");

                try
                {
                    switch (next.Type)
                    {
                        case ScreenFlowCommandType.Trigger:
                            if (next.Object == null)
                            {
                                Debug.LogError(
                                    "[CommandQueueProcessor:ProcessCommandQueue] -> Trigger command object is null");
                                next.IsDone = true;
                                break;
                            }

                            switch (next.Object)
                            {
                                case ScreenConfig screenConfig:
                                    await screenFlow.ScreenTransitTo(screenConfig, false, next.Args, next.OnShow,
                                        next.OnHide);
                                    next.IsDone = true;
                                    if (screenFlow.Verbose)
                                        Debug.Log(
                                            $"[CommandQueueProcessor:ProcessCommandQueue] -> Completed screen transition to: {screenConfig.name}");

                                    break;
                                case PopupConfig popupConfig:
                                    if (screenFlow.CurrentScreenConfig != null &&
                                        screenFlow.CurrentScreenConfig.AllowPopups)
                                    {
                                        await popupManager.PopupTransitTo(popupConfig, screenFlow.CurrentScreenConfig,
                                            next.Args, next.OnShow, next.OnHide, screenFlow);
                                        next.IsDone = true;
                                        if (screenFlow.Verbose)
                                            Debug.Log(
                                                $"[CommandQueueProcessor:ProcessCommandQueue] -> Completed popup transition to: {popupConfig.name}");
                                    }
                                    else
                                    {
                                        Debug.LogError(
                                            $"[CommandQueueProcessor:ProcessCommandQueue] -> Cannot show popup {popupConfig.name}: Current screen does not allow popups or is null");
                                    }

                                    break;
                                default:
                                    Debug.LogError(
                                        $"[CommandQueueProcessor:ProcessCommandQueue] -> Unsupported object type for trigger: {next.Object.GetType()}");
                                    next.IsDone = true;
                                    break;
                            }

                            break;
                        case ScreenFlowCommandType.MoveBack:
                            await MoveBackOp(next.Args, next.OnShow, next.OnHide);
                            next.IsDone = true;
                            if (screenFlow.Verbose)
                                Debug.Log(
                                    "[CommandQueueProcessor:ProcessCommandQueue] -> Completed move back operation");

                            break;
                        case ScreenFlowCommandType.ClosePopup:
                            if (next.Object is IPopupBase popupBase)
                            {
                                await popupManager.ClosePopupOp(popupBase, next.Args, next.OnShow, next.OnHide,
                                    screenFlow);
                                if (screenFlow.Verbose)
                                    Debug.Log(
                                        $"[CommandQueueProcessor:ProcessCommandQueue] -> Completed closing popup: {popupBase.GameObject.name}");
                            }
                            else
                            {
                                Debug.LogError(
                                    "[CommandQueueProcessor:ProcessCommandQueue] -> Invalid popup object for close command");
                            }

                            next.IsDone = true;
                            break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    next.IsDone = true;
                }
            }
        }

        private async Task MoveBackOp(object args, Action<IScreenBase> onShow, Action<IScreenBase> onHide)
        {
            if (screenFlow.ScreensHistory.Count < 2)
            {
                Debug.LogWarning("[CommandQueueProcessor:MoveBackOp] -> Cannot move back, insufficient screen history");
                return;
            }

            EntityArgPair<ScreenConfig> previousScreenConfig =
                screenFlow.ScreensHistory[screenFlow.ScreensHistory.Count - 2];
            if (previousScreenConfig == null)
            {
                Debug.LogError("[CommandQueueProcessor:MoveBackOp] -> Previous screen config is null");
                return;
            }

            if (screenFlow.CurrentScreenConfig == null)
            {
                Debug.LogError("[CommandQueueProcessor:MoveBackOp] -> Current screen config is null");
                return;
            }

            if (screenFlow.CurrentScreenConfig.CanMoveBackFromHere && previousScreenConfig.Entity.CanMoveBackToHere)
            {
                await screenFlow.ScreenTransitTo(previousScreenConfig.Entity, true, args ?? previousScreenConfig.Args,
                    onShow, onHide);
                if (screenFlow.Verbose)
                    Debug.Log(
                        $"[CommandQueueProcessor:MoveBackOp] -> Moved back to screen: {previousScreenConfig.Entity.name}");
            }
            else
            {
                Debug.LogWarning(
                    $"[CommandQueueProcessor:MoveBackOp] -> Move back not allowed from {screenFlow.CurrentScreenConfig.name} to {previousScreenConfig.Entity.name}");
            }
        }
    }
}
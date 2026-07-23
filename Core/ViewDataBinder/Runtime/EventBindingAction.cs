using System;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace LegendaryTools.ViewBinding
{
    [Serializable]
    public sealed class EventBindingAction
    {
        [SerializeField] private EventBindingActionKind actionKind;
        [SerializeField] private EventBindingActionParameterMode parameterMode;
        [SerializeField] private UnityEvent eventWithoutParameters = new UnityEvent();
        [SerializeField] private BindingObjectUnityEvent oldValueEvent = new BindingObjectUnityEvent();
        [SerializeField] private BindingObjectUnityEvent newValueEvent = new BindingObjectUnityEvent();
        [SerializeField] private BindingObjectPairUnityEvent oldAndNewValuesEvent = new BindingObjectPairUnityEvent();
        [SerializeField] private BindingInstanceReference taskMethodTarget = new BindingInstanceReference();
        [SerializeField] private string taskMethodSignature;
        [SerializeField] private bool preventConcurrentExecution = true;

        [NonSerialized] private MethodInfo cachedTaskMethod;
        [NonSerialized] private ParameterInfo[] cachedTaskParameters;
        [NonSerialized] private Type cachedTargetType;
        [NonSerialized] private bool cachedTargetIsStatic;
        [NonSerialized] private string cachedSignature;
        [NonSerialized] private Task runningTask;
        [NonSerialized] private List<Task> concurrentTasks;
        [NonSerialized] private object[] taskArguments;

        public EventBindingActionKind ActionKind => actionKind;

        public EventBindingActionParameterMode ParameterMode => parameterMode;

        public bool IsTaskRunning
        {
            get
            {
                if (runningTask != null && !runningTask.IsCompleted)
                {
                    return true;
                }

                if (concurrentTasks == null)
                {
                    return false;
                }

                for (int i = 0; i < concurrentTasks.Count; i++)
                {
                    if (!concurrentTasks[i].IsCompleted)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void Invoke(object oldValue, object newValue)
        {
            if (actionKind == EventBindingActionKind.UnityEvent)
            {
                InvokeUnityEvent(oldValue, newValue);
                return;
            }

            if (!TryInvoke(oldValue, newValue, out _, out string error))
            {
                throw new InvalidOperationException(error);
            }
        }

        public bool TryInvoke(
            object oldValue,
            object newValue,
            out bool taskRunning,
            out string error)
        {
            taskRunning = false;

            bool observationSucceeded = TryObserveTask(
                out bool existingTaskRunning,
                out error);
            taskRunning = existingTaskRunning;
            if (!observationSucceeded)
            {
                return false;
            }

            if (actionKind == EventBindingActionKind.UnityEvent)
            {
                try
                {
                    InvokeUnityEvent(oldValue, newValue);
                    error = string.Empty;
                    return true;
                }
                catch (Exception exception)
                {
                    error = GetInnermostMessage(exception);
                    return false;
                }
            }

            if (actionKind != EventBindingActionKind.TaskMethod)
            {
                error = $"Unsupported action kind: {actionKind}.";
                return false;
            }

            if (existingTaskRunning && preventConcurrentExecution)
            {
                taskRunning = true;
                error = string.Empty;
                return true;
            }

            if (existingTaskRunning && runningTask != null)
            {
                if (concurrentTasks == null)
                {
                    concurrentTasks = new List<Task>(2);
                }

                concurrentTasks.Add(runningTask);
                runningTask = null;
            }

            if (taskMethodTarget == null ||
                !taskMethodTarget.TryResolve(out BindingInstanceHandle handle, out error))
            {
                return false;
            }

            if (!TryGetTaskMethod(handle, out MethodInfo method, out error))
            {
                return false;
            }

            if (!TaskMethodBindingUtility.TryPrepareArguments(
                    cachedTaskParameters,
                    parameterMode,
                    oldValue,
                    newValue,
                    ref taskArguments,
                    out error))
            {
                return false;
            }

            object invocationResult;
            try
            {
                invocationResult = method.Invoke(
                    handle.IsStatic ? null : handle.Instance,
                    taskArguments);
            }
            catch (Exception exception)
            {
                runningTask = null;
                error = GetInnermostMessage(exception);
                return false;
            }
            finally
            {
                ClearTaskArguments();
            }

            if (!(invocationResult is Task task))
            {
                error = $"Task method '{method.Name}' returned null or a non-Task value.";
                return false;
            }

            runningTask = task;
            ObserveFault(task);
            if (!TryObserveTask(out taskRunning, out error))
            {
                return false;
            }

            return true;
        }

        public bool TryObserveTask(out bool taskRunning, out string error)
        {
            taskRunning = false;
            string firstError = null;

            if (concurrentTasks != null)
            {
                for (int i = concurrentTasks.Count - 1; i >= 0; i--)
                {
                    Task concurrentTask = concurrentTasks[i];
                    if (!concurrentTask.IsCompleted)
                    {
                        taskRunning = true;
                        continue;
                    }

                    concurrentTasks.RemoveAt(i);
                    try
                    {
                        concurrentTask.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        if (firstError == null)
                        {
                            firstError = GetInnermostMessage(exception);
                        }
                    }
                }
            }

            if (runningTask != null)
            {
                if (!runningTask.IsCompleted)
                {
                    taskRunning = true;
                }
                else
                {
                    Task completedTask = runningTask;
                    runningTask = null;

                    try
                    {
                        completedTask.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        if (firstError == null)
                        {
                            firstError = GetInnermostMessage(exception);
                        }
                    }
                }
            }

            error = firstError ?? string.Empty;
            return firstError == null;
        }

        public void ResetRuntimeState()
        {
            runningTask = null;
            concurrentTasks?.Clear();
            taskArguments = null;
            cachedTaskMethod = null;
            cachedTaskParameters = null;
            cachedTargetType = null;
            cachedSignature = null;
            cachedTargetIsStatic = false;
        }

        private void ClearTaskArguments()
        {
            if (taskArguments == null)
            {
                return;
            }

            for (int i = 0; i < taskArguments.Length; i++)
            {
                taskArguments[i] = null;
            }
        }

        private static void ObserveFault(Task task)
        {
            task.ContinueWith(
                completedTask =>
                {
                    _ = completedTask.Exception;
                },
                TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously);
        }

        private void InvokeUnityEvent(object oldValue, object newValue)
        {
            switch (parameterMode)
            {
                case EventBindingActionParameterMode.None:
                    eventWithoutParameters.Invoke();
                    break;

                case EventBindingActionParameterMode.OldValue:
                    oldValueEvent.Invoke(oldValue);
                    break;

                case EventBindingActionParameterMode.NewValue:
                    newValueEvent.Invoke(newValue);
                    break;

                case EventBindingActionParameterMode.OldAndNewValues:
                    oldAndNewValuesEvent.Invoke(oldValue, newValue);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private bool TryGetTaskMethod(
            BindingInstanceHandle handle,
            out MethodInfo method,
            out string error)
        {
            if (cachedTaskMethod != null &&
                cachedTargetType == handle.Type &&
                cachedTargetIsStatic == handle.IsStatic &&
                string.Equals(cachedSignature, taskMethodSignature, StringComparison.Ordinal))
            {
                method = cachedTaskMethod;
                error = string.Empty;
                return true;
            }

            if (!TaskMethodBindingUtility.TryResolveMethod(
                    handle,
                    taskMethodSignature,
                    parameterMode,
                    out method,
                    out error))
            {
                return false;
            }

            cachedTaskMethod = method;
            cachedTaskParameters = method.GetParameters();
            cachedTargetType = handle.Type;
            cachedTargetIsStatic = handle.IsStatic;
            cachedSignature = taskMethodSignature;
            return true;
        }

        private static string GetInnermostMessage(Exception exception)
        {
            while (exception.InnerException != null)
            {
                exception = exception.InnerException;
            }

            return exception.Message;
        }
    }
}

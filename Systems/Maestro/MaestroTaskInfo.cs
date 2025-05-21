using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace LegendaryTools.Maestro
{
    public class MaestroTaskInfo : IMaestroTaskInfo
    {
        public bool Enabled => MaestroTaskObject.Enabled;
        public bool RequiresInternet => MaestroTaskObject.RequiresInternet;
        public bool IsRunning { get; private set; }
        public bool IsCompleted { get; private set; }
        public bool HasError { get; private set; }
        public bool IsDone { get; private set; }
        public Exception Error { get; private set; }
        public float TimeSpentMilliseconds { get; private set; }
        public IMaestroTask MaestroTaskObject { get; private set; }
        public virtual bool HasPrerequisites =>
            DependenciesInternal.Count == 0 || DependenciesInternal.TrueForAll(item => item.IsCompleted);

        public List<MaestroTaskInfo> Dependencies => new List<MaestroTaskInfo>(DependenciesInternal);
        internal readonly List<MaestroTaskInfo> DependenciesInternal = new List<MaestroTaskInfo>();

        public event Action<MaestroTaskInfo, bool> OnTaskCompleted;

        public MaestroTaskInfo(IMaestroTask maestroTaskObject)
        {
            MaestroTaskObject = maestroTaskObject;
        }

        public MaestroTaskInfo(IMaestroTask maestroTaskObject, params MaestroTaskInfo[] dependencies)
        {
            MaestroTaskObject = maestroTaskObject;
            this.DependenciesInternal.AddRange(dependencies);
        }

        public async Task DoTaskOperation()
        {
            if (!HasPrerequisites || IsCompleted || IsRunning || HasError) return;

            IsRunning = true;
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                bool taskResult = false;
                Task<bool> task = null;
                if (MaestroTaskObject.TimeOut > 0)
                {
                    task = MaestroTaskObject.ThreadSafe
                        ? Task.Run(MaestroTaskObject.DoTaskOperation)
                        : MaestroTaskObject.DoTaskOperation();

                    if (await Task.WhenAny(task, Task.Delay(MaestroTaskObject.TimeOut * 1000)) != task)
                    {
                        taskResult = false;
                        throw new TimeoutException(
                            $"{MaestroTaskObject.GetType()} has time out while doing orchestrable task.");
                    }

                    taskResult = task.Result;
                }
                else
                {
                    task = MaestroTaskObject.ThreadSafe
                        ? Task.Run(MaestroTaskObject.DoTaskOperation)
                        : MaestroTaskObject.DoTaskOperation();

                    taskResult = await task;
                }

                if (!taskResult)
                    Debugger.LogError<Maestro>(
                        $"[{nameof(MaestroTaskInfo)}:{nameof(DoTaskOperation)}] -> {MaestroTaskObject.GetType()} dependencies were not met in order to complete the task.");
            }
            catch (Exception e)
            {
                IsRunning = false;
                HasError = true;
                IsDone = true;
                Error = e;
                 Debugger.LogError<Maestro>(
                     $"[{nameof(MaestroTaskInfo)}:{nameof(DoTaskOperation)}] -> {MaestroTaskObject.GetType()} got a error while doing a task.");
                Debugger.LogException<Maestro>(e);
                sw.Stop();
                TimeSpentMilliseconds = sw.Elapsed.Milliseconds;
                OnTaskCompleted?.Invoke(this, false);
                return;
            }

            sw.Stop();
            TimeSpentMilliseconds = sw.Elapsed.Milliseconds;
            
            Debugger.Log<Maestro>(
                $"[{nameof(MaestroTaskInfo)}:{nameof(DoTaskOperation)}] -> {MaestroTaskObject.GetType()} finished task, time: {sw.Elapsed.TotalSeconds} seconds");

            IsRunning = false;
            IsCompleted = true;
            IsDone = true;
            OnTaskCompleted?.Invoke(this, true);
        }
    }
}
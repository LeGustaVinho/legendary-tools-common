using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LegendaryTools.Maestro
{
    public interface IMaestroTaskInfo
    {
        bool Enabled { get; }
        bool RequiresInternet { get; }
        bool IsRunning { get; }
        bool IsCompleted { get; }
        bool HasError { get; }
        bool IsDone { get; }
        Exception Error { get; }
        float TimeSpentMilliseconds { get; }
        IMaestroTask MaestroTaskObject { get; }
        bool HasPrerequisites { get; }
        List<MaestroTaskInfo> Dependencies { get; }
        event Action<MaestroTaskInfo, bool> OnTaskCompleted;
        Task DoTaskOperation();
    }
}
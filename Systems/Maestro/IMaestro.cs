using System;
using System.Threading.Tasks;

namespace LegendaryTools.Maestro
{
    public interface IMaestro : IDisposable
    {
        event Action<Maestro, bool> OnFinished;
        event Action<MaestroTaskInfo, bool> OnTaskFinished;
        void Add(IMaestroTask task, params IMaestroTask[] dependencies);
        void Add(params IMaestroTaskWithDependency[] tasks);
        void Add(params IMaestroTask[] tasks);
        Task Start();
    }
}
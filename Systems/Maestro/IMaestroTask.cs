using System.Threading.Tasks;

namespace LegendaryTools.Maestro
{
    public interface IMaestroTask
    {
        bool Enabled { get; }
        int TimeOut { get; }
        bool ThreadSafe { get; }
        bool RequiresInternet { get; }
        Task<bool> DoTaskOperation();
    }
}
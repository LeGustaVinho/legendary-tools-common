namespace LegendaryTools
{
    public interface IWeaveExec
    {
        WeaveExecType WeaveExecType { get; }
        
        void RunWeaver();
    }
}
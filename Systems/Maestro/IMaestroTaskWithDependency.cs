namespace LegendaryTools.Maestro
{
    public interface IMaestroTaskWithDependency : IMaestroTask
    {
        IMaestroTask[] Dependencies { get; set; }
    }
}
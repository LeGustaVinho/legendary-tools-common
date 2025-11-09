namespace LegendaryTools.SOAP.Variables
{
    /// <summary>
    /// Read-only interface for variables.
    /// </summary>
    public interface IReadOnlyVariable<out T>
    {
        /// <summary>Current runtime value.</summary>
        T Value { get; }
    }
}
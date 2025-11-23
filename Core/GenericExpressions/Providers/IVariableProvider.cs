namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Abstraction for external variable providers.
    /// You can chain multiple providers in the evaluation context.
    /// </summary>
    public interface IVariableProvider<T>
    {
        /// <summary>
        /// Attempts to resolve a variable value by name.
        /// </summary>
        /// <param name="name">Variable name.</param>
        /// <param name="value">Resolved value, if found.</param>
        /// <returns>True if the variable was resolved; otherwise false.</returns>
        bool TryGetVariable(string name, out T value);
    }
}
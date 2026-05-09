using System;

namespace LegendaryTools.MiniCSharp
{
    /// <summary>
    /// Represents script-level syntax and runtime failures.
    /// </summary>
    public sealed class ScriptException : Exception
    {
        public ScriptException(string message) : base(message)
        {
        }
    }
}
using System;

namespace LegendaryTools.MiniCSharp
{
    /// <summary>
    /// Represents a parsed script that can be executed repeatedly without lexing or parsing again.
    /// </summary>
    public sealed class RuntimeScript
    {
        private readonly ScriptProgram _program;

        internal RuntimeScript(string source, ScriptProgram program)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            _program = program ?? throw new ArgumentNullException(nameof(program));
            CompiledAtUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets the original source used to compile this script.
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// Gets the UTC time when this script was compiled.
        /// </summary>
        public DateTime CompiledAtUtc { get; }

        internal void Execute(ScriptContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _program.Execute(context);
        }
    }
}

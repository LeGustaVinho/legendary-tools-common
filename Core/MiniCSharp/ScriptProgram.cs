using System.Collections.Generic;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class ScriptProgram : Statement
    {
        private readonly List<Statement> _statements;

        public ScriptProgram(List<Statement> statements)
        {
            _statements = statements;
        }

        public override void Execute(ScriptContext context)
        {
            try
            {
                foreach (Statement statement in _statements)
                {
                    statement.Execute(context);
                }
            }
            catch (ScriptReturnException)
            {
                return;
            }
        }
    }
}

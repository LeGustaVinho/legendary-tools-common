using System.Collections.Generic;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class BlockStatement : Statement
    {
        private readonly List<Statement> _statements;

        public BlockStatement(List<Statement> statements)
        {
            _statements = statements;
        }

        public override void Execute(ScriptContext context)
        {
            context.PushScope();

            try
            {
                foreach (Statement statement in _statements)
                {
                    statement.Execute(context);
                }
            }
            finally
            {
                context.PopScope();
            }
        }
    }
}
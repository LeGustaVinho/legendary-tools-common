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
                DeclareFunctions(context);

                foreach (Statement statement in _statements)
                {
                    if (statement is FunctionDeclarationStatement)
                    {
                        continue;
                    }

                    statement.Execute(context);
                }
            }
            finally
            {
                context.PopScope();
            }
        }

        public override System.Collections.IEnumerator ExecuteCoroutine(ScriptContext context)
        {
            context.PushScope();

            try
            {
                DeclareFunctions(context);

                foreach (Statement statement in _statements)
                {
                    if (statement is FunctionDeclarationStatement)
                    {
                        continue;
                    }

                    System.Collections.IEnumerator enumerator = statement.ExecuteCoroutine(context);

                    while (enumerator.MoveNext())
                    {
                        yield return enumerator.Current;
                    }
                }
            }
            finally
            {
                context.PopScope();
            }
        }

        private void DeclareFunctions(ScriptContext context)
        {
            foreach (Statement statement in _statements)
            {
                if (statement is FunctionDeclarationStatement functionDeclaration)
                {
                    functionDeclaration.Declare(context);
                }
            }
        }
    }
}

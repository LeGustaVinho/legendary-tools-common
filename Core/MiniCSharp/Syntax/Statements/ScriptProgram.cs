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
            catch (ScriptReturnException)
            {
                return;
            }
        }

        public override System.Collections.IEnumerator ExecuteCoroutine(ScriptContext context)
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

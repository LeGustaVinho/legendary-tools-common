namespace LegendaryTools.MiniCSharp
{
    internal sealed class ForStatement : Statement
    {
        private const int MaxIterations = 1000000;

        private readonly Statement _initializer;
        private readonly Expression _condition;
        private readonly Expression _increment;
        private readonly Statement _body;

        public ForStatement(Statement initializer, Expression condition, Expression increment, Statement body)
        {
            _initializer = initializer;
            _condition = condition;
            _increment = increment;
            _body = body;
        }

        public override void Execute(ScriptContext context)
        {
            context.PushScope();

            try
            {
                _initializer?.Execute(context);

                int iterationCount = 0;

                while (_condition == null || RuntimeConversion.ToBool(_condition.Evaluate(context).Value))
                {
                    if (++iterationCount > MaxIterations)
                    {
                        throw new ScriptException($"For loop exceeded the safety limit of {MaxIterations} iterations.");
                    }

                    try
                    {
                        _body.Execute(context);
                    }
                    catch (ScriptContinueException)
                    {
                    }
                    catch (ScriptBreakException)
                    {
                        break;
                    }

                    _increment?.Evaluate(context);
                }
            }
            finally
            {
                context.PopScope();
            }
        }
    }
}

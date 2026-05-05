namespace LegendaryTools.MiniCSharp
{
    internal abstract class Statement
    {
        public abstract void Execute(ScriptContext context);
    }

    internal sealed class WhileStatement : Statement
    {
        private const int MaxIterations = 1000000;

        private readonly Expression _condition;
        private readonly Statement _body;

        public WhileStatement(Expression condition, Statement body)
        {
            _condition = condition;
            _body = body;
        }

        public override void Execute(ScriptContext context)
        {
            int iterationCount = 0;

            while (RuntimeConversion.ToBool(_condition.Evaluate(context).Value))
            {
                if (++iterationCount > MaxIterations)
                {
                    throw new ScriptException($"While loop exceeded the safety limit of {MaxIterations} iterations.");
                }

                try
                {
                    _body.Execute(context);
                }
                catch (ScriptContinueException)
                {
                    continue;
                }
                catch (ScriptBreakException)
                {
                    break;
                }
            }
        }
    }

    internal sealed class BreakStatement : Statement
    {
        public override void Execute(ScriptContext context)
        {
            throw new ScriptBreakException();
        }
    }

    internal sealed class ContinueStatement : Statement
    {
        public override void Execute(ScriptContext context)
        {
            throw new ScriptContinueException();
        }
    }

    internal sealed class ReturnStatement : Statement
    {
        private readonly Expression _value;
        private readonly bool _hasExpression;

        public ReturnStatement(Expression value, bool hasExpression)
        {
            _value = value;
            _hasExpression = hasExpression;
        }

        public override void Execute(ScriptContext context)
        {
            RuntimeValue returnValue = _value != null
                ? _value.Evaluate(context)
                : RuntimeValue.From(null);

            throw new ScriptReturnException(returnValue, _hasExpression);
        }
    }

    internal sealed class ScriptBreakException : System.Exception
    {
    }

    internal sealed class ScriptContinueException : System.Exception
    {
    }

    internal sealed class ScriptReturnException : System.Exception
    {
        public ScriptReturnException(RuntimeValue returnValue, bool hasExpression)
        {
            ReturnValue = returnValue;
            HasExpression = hasExpression;
        }

        public RuntimeValue ReturnValue { get; }

        public bool HasExpression { get; }
    }
}

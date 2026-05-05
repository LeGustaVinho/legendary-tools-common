namespace LegendaryTools.MiniCSharp
{
    internal sealed class CompoundAssignmentExpression : Expression
    {
        private readonly IAssignableExpression _target;
        private readonly Token _operatorToken;
        private readonly Expression _value;

        public CompoundAssignmentExpression(IAssignableExpression target, Token operatorToken, Expression value)
        {
            _target = target;
            _operatorToken = operatorToken;
            _value = value;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            if (_target is MemberExpression memberExpression &&
                memberExpression.TryApplyCompoundAssignment(context, _operatorToken.Type, _value, out RuntimeValue eventResult))
            {
                return eventResult;
            }

            RuntimeValue currentValue = _target.GetValue(context);
            RuntimeValue rightValue = _value.Evaluate(context);

            object result;

            switch (_operatorToken.Type)
            {
                case TokenType.PlusEqual:
                    result = RuntimeOperators.Add(currentValue.Value, rightValue.Value);
                    break;

                case TokenType.MinusEqual:
                    result = RuntimeOperators.Subtract(currentValue.Value, rightValue.Value);
                    break;

                case TokenType.StarEqual:
                    result = RuntimeOperators.Multiply(currentValue.Value, rightValue.Value);
                    break;

                case TokenType.SlashEqual:
                    result = RuntimeOperators.Divide(currentValue.Value, rightValue.Value);
                    break;

                case TokenType.PercentEqual:
                    result = RuntimeOperators.Modulo(currentValue.Value, rightValue.Value);
                    break;

                default:
                    throw new ScriptException($"Unsupported compound assignment operator '{_operatorToken.Lexeme}'.");
            }

            _target.Assign(context, result);
            return _target.GetValue(context);
        }
    }
}

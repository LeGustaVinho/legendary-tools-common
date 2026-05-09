namespace LegendaryTools.MiniCSharp
{
    internal sealed class UnaryExpression : Expression
    {
        private readonly Token _operatorToken;
        private readonly Expression _right;

        public UnaryExpression(Token operatorToken, Expression right)
        {
            _operatorToken = operatorToken;
            _right = right;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            RuntimeValue right = _right.Evaluate(context);

            switch (_operatorToken.Type)
            {
                case TokenType.Bang:
                    return RuntimeValue.From(!RuntimeConversion.ToBool(right.Value));

                case TokenType.Minus:
                    return RuntimeValue.From(RuntimeOperators.Negate(right.Value));

                default:
                    throw new ScriptException($"Unsupported unary operator '{_operatorToken.Lexeme}'.");
            }
        }
    }
}
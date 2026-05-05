namespace LegendaryTools.MiniCSharp
{
    internal sealed class BinaryExpression : Expression
    {
        private readonly Expression _left;
        private readonly Token _operatorToken;
        private readonly Expression _right;

        public BinaryExpression(Expression left, Token operatorToken, Expression right)
        {
            _left = left;
            _operatorToken = operatorToken;
            _right = right;
        }

        public override RuntimeValue Evaluate(ScriptContext context)
        {
            if (_operatorToken.Type == TokenType.AndAnd)
            {
                bool leftBool = RuntimeConversion.ToBool(_left.Evaluate(context).Value);
                return RuntimeValue.From(leftBool && RuntimeConversion.ToBool(_right.Evaluate(context).Value));
            }

            if (_operatorToken.Type == TokenType.OrOr)
            {
                bool leftBool = RuntimeConversion.ToBool(_left.Evaluate(context).Value);
                return RuntimeValue.From(leftBool || RuntimeConversion.ToBool(_right.Evaluate(context).Value));
            }

            object left = _left.Evaluate(context).Value;
            object right = _right.Evaluate(context).Value;

            switch (_operatorToken.Type)
            {
                case TokenType.Plus:
                    return RuntimeValue.From(RuntimeOperators.Add(left, right));

                case TokenType.Minus:
                    return RuntimeValue.From(RuntimeOperators.Subtract(left, right));

                case TokenType.Star:
                    return RuntimeValue.From(RuntimeOperators.Multiply(left, right));

                case TokenType.Slash:
                    return RuntimeValue.From(RuntimeOperators.Divide(left, right));

                case TokenType.Percent:
                    return RuntimeValue.From(RuntimeOperators.Modulo(left, right));

                case TokenType.EqualEqual:
                    return RuntimeValue.From(RuntimeOperators.AreEqual(left, right));

                case TokenType.BangEqual:
                    return RuntimeValue.From(!RuntimeOperators.AreEqual(left, right));

                case TokenType.Greater:
                    return RuntimeValue.From(RuntimeOperators.Compare(left, right) > 0);

                case TokenType.GreaterEqual:
                    return RuntimeValue.From(RuntimeOperators.Compare(left, right) >= 0);

                case TokenType.Less:
                    return RuntimeValue.From(RuntimeOperators.Compare(left, right) < 0);

                case TokenType.LessEqual:
                    return RuntimeValue.From(RuntimeOperators.Compare(left, right) <= 0);

                default:
                    throw new ScriptException($"Unsupported binary operator '{_operatorToken.Lexeme}'.");
            }
        }
    }
}
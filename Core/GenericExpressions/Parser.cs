using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Recursive descent parser with extended operator precedence:
    /// - Lowest: assignment (=) and semicolon-separated sequences
    /// - Logical OR: or, ||
    /// - Logical AND: and, &&
    /// - Equality: ==, !=
    /// - Relational: <, <=, >, >=
    /// - Additive: +, -
    /// - Multiplicative: *, /
    /// - Power: ^
    /// - Unary: +, -, not, !
    /// Also supports:
    /// - Boolean literals: true, false
    /// - Variables (identifiers starting with '$')
    /// - Function calls: name(...)
    /// - Parentheses: ( ... )
    /// </summary>
    internal sealed class Parser<T>
    {
        private readonly List<Token> _tokens;
        private readonly INumberOperations<T> _ops;
        private int _position;

        public Parser(List<Token> tokens, INumberOperations<T> ops)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _ops = ops ?? throw new ArgumentNullException(nameof(ops));
            _position = 0;
        }

        public ExpressionNode<T> ParseExpression()
        {
            // First parse an assignment-level expression
            ExpressionNode<T> first = ParseAssignment();

            // Check for semicolon-separated sequence
            if (Peek().Kind == TokenKind.Semicolon)
            {
                List<ExpressionNode<T>> statements = new() { first };

                // Consume all ';' and following assignments
                while (Peek().Kind == TokenKind.Semicolon)
                {
                    Consume(); // consume ';'

                    // Allow trailing semicolon at the end: "expr;"
                    if (Peek().Kind == TokenKind.EndOfInput) break;

                    ExpressionNode<T> nextStatement = ParseAssignment();
                    statements.Add(nextStatement);
                }

                // After a sequence, we expect end of input
                Token finalToken = Peek();
                if (finalToken.Kind != TokenKind.EndOfInput)
                    throw new InvalidOperationException($"Unexpected token '{finalToken.Text}' at end of expression.");

                if (statements.Count == 1) return statements[0];

                return new SequenceNode<T>(statements);
            }
            else
            {
                // Single expression, no sequence
                Token current = Peek();
                if (current.Kind != TokenKind.EndOfInput)
                    throw new InvalidOperationException($"Unexpected token '{current.Text}' at end of expression.");

                return first;
            }
        }

        /// <summary>
        /// Parses assignment expressions:
        /// - $x = 2 * $y + 3
        /// - $x = $y = 3 (right-associative)
        /// If there is no '=', falls back to logical OR.
        /// Assignment has the lowest precedence.
        /// </summary>
        private ExpressionNode<T> ParseAssignment()
        {
            Token token = Peek();

            // Look for pattern: Identifier '=' ...
            if (token.Kind == TokenKind.Identifier)
            {
                Token next = Peek(1);
                if (next.Kind == TokenKind.Equals)
                {
                    // Assignment
                    Consume(); // identifier
                    Consume(); // '='

                    string name = token.Text;

                    if (!name.StartsWith("$", StringComparison.Ordinal))
                        throw new InvalidOperationException($"Variable '{name}' must start with '$' prefix.");

                    // Right-associative assignment: $x = $y = 3
                    ExpressionNode<T> valueExpr = ParseAssignment();

                    return new AssignmentNode<T>(name, valueExpr);
                }
            }

            // No assignment pattern, parse logical OR
            return ParseLogicalOr();
        }

        /// <summary>
        /// Logical OR: expr or expr, expr || expr
        /// </summary>
        private ExpressionNode<T> ParseLogicalOr()
        {
            ExpressionNode<T> node = ParseLogicalAnd();

            while (true)
            {
                Token token = Peek();
                if (token.Kind == TokenKind.Or)
                {
                    Consume();
                    ExpressionNode<T> right = ParseLogicalAnd();
                    node = new LogicalBinaryNode<T>(LogicalBinaryOperator.Or, node, right);
                }
                else
                {
                    break;
                }
            }

            return node;
        }

        /// <summary>
        /// Logical AND: expr and expr, expr && expr
        /// </summary>
        private ExpressionNode<T> ParseLogicalAnd()
        {
            ExpressionNode<T> node = ParseEquality();

            while (true)
            {
                Token token = Peek();
                if (token.Kind == TokenKind.And)
                {
                    Consume();
                    ExpressionNode<T> right = ParseEquality();
                    node = new LogicalBinaryNode<T>(LogicalBinaryOperator.And, node, right);
                }
                else
                {
                    break;
                }
            }

            return node;
        }

        /// <summary>
        /// Equality: ==, !=
        /// </summary>
        private ExpressionNode<T> ParseEquality()
        {
            ExpressionNode<T> node = ParseRelational();

            while (true)
            {
                Token token = Peek();
                if (token.Kind == TokenKind.EqualEqual)
                {
                    Consume();
                    ExpressionNode<T> right = ParseRelational();
                    node = new RelationalNode<T>(RelationalOperator.Equal, node, right);
                }
                else if (token.Kind == TokenKind.BangEqual)
                {
                    Consume();
                    ExpressionNode<T> right = ParseRelational();
                    node = new RelationalNode<T>(RelationalOperator.NotEqual, node, right);
                }
                else
                {
                    break;
                }
            }

            return node;
        }

        /// <summary>
        /// Relational: <, <=, >, >=
        /// </summary>
        private ExpressionNode<T> ParseRelational()
        {
            ExpressionNode<T> node = ParseAdditive();

            while (true)
            {
                Token token = Peek();
                RelationalOperator op;

                if (token.Kind == TokenKind.Less)
                    op = RelationalOperator.Less;
                else if (token.Kind == TokenKind.LessOrEqual)
                    op = RelationalOperator.LessOrEqual;
                else if (token.Kind == TokenKind.Greater)
                    op = RelationalOperator.Greater;
                else if (token.Kind == TokenKind.GreaterOrEqual)
                    op = RelationalOperator.GreaterOrEqual;
                else
                    break;

                Consume();
                ExpressionNode<T> right = ParseAdditive();
                node = new RelationalNode<T>(op, node, right);
            }

            return node;
        }

        private ExpressionNode<T> ParseAdditive()
        {
            ExpressionNode<T> node = ParseMultiplicative();

            while (true)
            {
                Token token = Peek();
                if (token.Kind == TokenKind.Plus)
                {
                    Consume();
                    ExpressionNode<T> right = ParseMultiplicative();
                    node = new BinaryNode<T>(BinaryOperator.Add, node, right);
                }
                else if (token.Kind == TokenKind.Minus)
                {
                    Consume();
                    ExpressionNode<T> right = ParseMultiplicative();
                    node = new BinaryNode<T>(BinaryOperator.Subtract, node, right);
                }
                else
                {
                    break;
                }
            }

            return node;
        }

        private ExpressionNode<T> ParseMultiplicative()
        {
            ExpressionNode<T> node = ParsePower();

            while (true)
            {
                Token token = Peek();
                if (token.Kind == TokenKind.Star)
                {
                    Consume();
                    ExpressionNode<T> right = ParsePower();
                    node = new BinaryNode<T>(BinaryOperator.Multiply, node, right);
                }
                else if (token.Kind == TokenKind.Slash)
                {
                    Consume();
                    ExpressionNode<T> right = ParsePower();
                    node = new BinaryNode<T>(BinaryOperator.Divide, node, right);
                }
                else
                {
                    break;
                }
            }

            return node;
        }

        private ExpressionNode<T> ParsePower()
        {
            // Right-associative: a ^ b ^ c = a ^ (b ^ c)
            ExpressionNode<T> node = ParseUnary();

            while (true)
            {
                Token token = Peek();
                if (token.Kind == TokenKind.Caret)
                {
                    Consume();
                    ExpressionNode<T> right = ParsePower();
                    node = new BinaryNode<T>(BinaryOperator.Power, node, right);
                }
                else
                {
                    break;
                }
            }

            return node;
        }

        private ExpressionNode<T> ParseUnary()
        {
            Token token = Peek();
            if (token.Kind == TokenKind.Plus)
            {
                Consume();
                ExpressionNode<T> operand = ParseUnary();
                return new UnaryNode<T>(UnaryOperator.Plus, operand);
            }
            else if (token.Kind == TokenKind.Minus)
            {
                Consume();
                ExpressionNode<T> operand = ParseUnary();
                return new UnaryNode<T>(UnaryOperator.Minus, operand);
            }
            else if (token.Kind == TokenKind.Not)
            {
                Consume();
                ExpressionNode<T> operand = ParseUnary();
                return new LogicalNotNode<T>(operand);
            }

            return ParsePrimary();
        }

        private ExpressionNode<T> ParsePrimary()
        {
            Token token = Peek();

            if (token.Kind == TokenKind.Number)
            {
                Consume();
                T value = _ops.ParseLiteral(token.Text);
                return new ConstantNode<T>(value);
            }

            if (token.Kind == TokenKind.True)
            {
                Consume();
                return new BooleanLiteralNode<T>(true);
            }

            if (token.Kind == TokenKind.False)
            {
                Consume();
                return new BooleanLiteralNode<T>(false);
            }

            if (token.Kind == TokenKind.Identifier) return ParseIdentifierOrFunctionCall();

            if (token.Kind == TokenKind.LParen)
            {
                Consume();
                ExpressionNode<T> expr = ParseAssignment();
                Token closing = Peek();
                if (closing.Kind != TokenKind.RParen)
                    throw new InvalidOperationException("Missing closing parenthesis.");
                Consume();
                return expr;
            }

            throw new InvalidOperationException($"Unexpected token '{token.Text}'.");
        }

        private ExpressionNode<T> ParseIdentifierOrFunctionCall()
        {
            Token ident = Peek();
            if (ident.Kind != TokenKind.Identifier) throw new InvalidOperationException("Identifier expected.");

            Consume();
            string name = ident.Text;

            // Function call: name(...)
            if (Peek().Kind == TokenKind.LParen)
            {
                Consume(); // consume '('
                List<ExpressionNode<T>> args = new();

                if (Peek().Kind != TokenKind.RParen)
                    while (true)
                    {
                        ExpressionNode<T> argExpr = ParseAssignment();
                        args.Add(argExpr);

                        Token token = Peek();
                        if (token.Kind == TokenKind.Comma)
                        {
                            Consume();
                            continue;
                        }

                        if (token.Kind == TokenKind.RParen) break;

                        throw new InvalidOperationException($"Unexpected token '{token.Text}' in argument list.");
                    }

                // Consume ')'
                if (Peek().Kind != TokenKind.RParen)
                    throw new InvalidOperationException("Missing closing parenthesis after function arguments.");

                Consume();
                return new FunctionCallNode<T>(name, args);
            }

            // Variable reference must start with '$'
            if (!name.StartsWith("$", StringComparison.Ordinal))
                throw new InvalidOperationException($"Variable '{name}' must start with '$' prefix.");

            return new VariableNode<T>(name);
        }

        private Token Peek()
        {
            return Peek(0);
        }

        private Token Peek(int offset)
        {
            int index = _position + offset;
            if (index >= _tokens.Count) return new Token(TokenKind.EndOfInput, string.Empty);

            return _tokens[index];
        }

        private Token Consume()
        {
            Token t = Peek();
            _position++;
            return t;
        }
    }
}
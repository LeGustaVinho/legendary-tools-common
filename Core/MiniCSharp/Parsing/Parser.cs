using System;
using System.Collections.Generic;
using System.Text;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly Func<string, Type> _resolveType;
        private int _current;
        private int _loopDepth;
        private int _switchDepth;
        private int _coroutineFunctionDepth;

        public Parser(List<Token> tokens, Func<string, Type> resolveType)
        {
            _tokens = tokens;
            _resolveType = resolveType;
        }

        public ScriptProgram ParseProgram()
        {
            var statements = new List<Statement>();

            while (!IsAtEnd())
            {
                statements.Add(ParseStatement());
            }

            return new ScriptProgram(statements);
        }

        private Statement ParseStatement()
        {
            if (Match(TokenType.If))
            {
                return ParseIfStatement();
            }

            if (Match(TokenType.For))
            {
                return ParseForStatement();
            }

            if (Match(TokenType.Try))
            {
                return ParseTryStatement();
            }

            if (Match(TokenType.Switch))
            {
                return ParseSwitchStatement();
            }

            if (Match(TokenType.Foreach))
            {
                return ParseForeachStatement();
            }

            if (Match(TokenType.While))
            {
                return ParseWhileStatement();
            }

            if (Match(TokenType.Break))
            {
                return ParseBreakStatement();
            }

            if (Match(TokenType.Continue))
            {
                return ParseContinueStatement();
            }

            if (Match(TokenType.Return))
            {
                return ParseReturnStatement();
            }

            if (Match(TokenType.Yield))
            {
                return ParseYieldStatement();
            }

            if (Match(TokenType.LeftBrace))
            {
                return new BlockStatement(ParseBlockStatements());
            }

            if (IsFunctionDeclarationStart())
            {
                return ParseFunctionDeclaration();
            }

            if (IsVariableDeclarationStart())
            {
                return ParseVariableDeclaration(consumeSemicolon: true);
            }

            return ParseExpressionStatement();
        }

        private IfStatement ParseIfStatement()
        {
            Consume(TokenType.LeftParen, "Expected '(' after 'if'.");
            Expression condition = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after if condition.");

            Statement thenBranch = ParseStatement();
            Statement elseBranch = null;

            if (Match(TokenType.Else))
            {
                elseBranch = ParseStatement();
            }

            return new IfStatement(condition, thenBranch, elseBranch);
        }

        private ForStatement ParseForStatement()
        {
            Consume(TokenType.LeftParen, "Expected '(' after 'for'.");

            Statement initializer = null;

            if (Match(TokenType.Semicolon))
            {
                initializer = null;
            }
            else
            {
                if (IsVariableDeclarationStart())
                {
                    initializer = ParseVariableDeclaration(consumeSemicolon: false);
                }
                else
                {
                    initializer = new ExpressionStatement(ParseExpression());
                }

                Consume(TokenType.Semicolon, "Expected ';' after for initializer.");
            }

            Expression condition = null;

            if (!Check(TokenType.Semicolon))
            {
                condition = ParseExpression();
            }

            Consume(TokenType.Semicolon, "Expected ';' after for condition.");

            Expression increment = null;

            if (!Check(TokenType.RightParen))
            {
                increment = ParseExpression();
            }

            Consume(TokenType.RightParen, "Expected ')' after for clauses.");

            _loopDepth++;
            Statement body;

            try
            {
                body = ParseStatement();
            }
            finally
            {
                _loopDepth--;
            }

            return new ForStatement(initializer, condition, increment, body);
        }

        private WhileStatement ParseWhileStatement()
        {
            Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
            Expression condition = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after while condition.");

            _loopDepth++;
            Statement body;

            try
            {
                body = ParseStatement();
            }
            finally
            {
                _loopDepth--;
            }

            return new WhileStatement(condition, body);
        }

        private TryStatement ParseTryStatement()
        {
            Statement tryBlock = ParseRequiredBlock("Expected '{' after 'try'.");
            Type catchType = null;
            string catchVariableName = null;
            Statement catchBlock = null;
            Statement finallyBlock = null;

            if (Match(TokenType.Catch))
            {
                if (Match(TokenType.LeftParen))
                {
                    string catchTypeName;
                    catchType = ParseTypeName(out catchTypeName, "Expected exception type in catch clause.");

                    if (catchType == null)
                    {
                        throw Error(Previous(), $"Unknown type '{catchTypeName}'.");
                    }

                    if (Check(TokenType.Identifier))
                    {
                        catchVariableName = Advance().Lexeme;
                    }

                    Consume(TokenType.RightParen, "Expected ')' after catch clause.");
                }

                catchBlock = ParseRequiredBlock("Expected '{' after 'catch'.");
            }

            if (Match(TokenType.Finally))
            {
                finallyBlock = ParseRequiredBlock("Expected '{' after 'finally'.");
            }

            if (catchBlock == null && finallyBlock == null)
            {
                throw Error(Peek(), "A try statement requires at least one catch or finally block.");
            }

            return new TryStatement(tryBlock, catchType, catchVariableName, catchBlock, finallyBlock);
        }

        private Statement ParseRequiredBlock(string message)
        {
            if (!Match(TokenType.LeftBrace))
            {
                throw Error(Peek(), message);
            }

            return new BlockStatement(ParseBlockStatements());
        }

        private SwitchStatement ParseSwitchStatement()
        {
            Consume(TokenType.LeftParen, "Expected '(' after 'switch'.");
            Expression target = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after switch expression.");
            Consume(TokenType.LeftBrace, "Expected '{' after switch expression.");

            var sections = new List<SwitchSection>();
            bool hasDefaultSection = false;
            _switchDepth++;

            try
            {
                while (!Check(TokenType.RightBrace) && !IsAtEnd())
                {
                    var labels = new List<SwitchLabel>();

                    while (true)
                    {
                        if (Match(TokenType.Case))
                        {
                            Expression caseExpression = ParseExpression();
                            Consume(TokenType.Colon, "Expected ':' after case label.");
                            labels.Add(SwitchLabel.ForCase(caseExpression));
                        }
                        else if (Match(TokenType.Default))
                        {
                            if (hasDefaultSection)
                            {
                                throw Error(Previous(), "A switch statement can only contain one default label.");
                            }

                            Consume(TokenType.Colon, "Expected ':' after default label.");
                            labels.Add(SwitchLabel.ForDefault());
                            hasDefaultSection = true;
                        }
                        else
                        {
                            throw Error(Peek(), "Expected 'case' or 'default' inside switch statement.");
                        }

                        if (!Check(TokenType.Case) && !Check(TokenType.Default))
                        {
                            break;
                        }
                    }

                    var statements = new List<Statement>();

                    while (!Check(TokenType.Case) &&
                           !Check(TokenType.Default) &&
                           !Check(TokenType.RightBrace) &&
                           !IsAtEnd())
                    {
                        statements.Add(ParseStatement());
                    }

                    sections.Add(new SwitchSection(labels, statements));
                }
            }
            finally
            {
                _switchDepth--;
            }

            Consume(TokenType.RightBrace, "Expected '}' after switch body.");
            return new SwitchStatement(target, sections);
        }

        private ForeachStatement ParseForeachStatement()
        {
            Consume(TokenType.LeftParen, "Expected '(' after 'foreach'.");

            bool inferType = Match(TokenType.Var);
            Type declaredType = null;

            if (!inferType)
            {
                string typeName;
                declaredType = ParseTypeName(out typeName, "Expected foreach variable type.");

                if (declaredType == null)
                {
                    throw Error(Previous(), $"Unknown type '{typeName}'.");
                }
            }

            Token name = Consume(TokenType.Identifier, "Expected foreach variable name.");
            Consume(TokenType.In, "Expected 'in' after foreach variable.");
            Expression enumerableExpression = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after foreach clauses.");

            _loopDepth++;
            Statement body;

            try
            {
                body = ParseStatement();
            }
            finally
            {
                _loopDepth--;
            }

            return new ForeachStatement(inferType, declaredType, name.Lexeme, enumerableExpression, body);
        }

        private BreakStatement ParseBreakStatement()
        {
            if (_loopDepth == 0 && _switchDepth == 0)
            {
                throw Error(Previous(), "'break' can only be used inside loops or switch statements.");
            }

            Consume(TokenType.Semicolon, "Expected ';' after 'break'.");
            return new BreakStatement();
        }

        private ContinueStatement ParseContinueStatement()
        {
            if (_loopDepth == 0)
            {
                throw Error(Previous(), "'continue' can only be used inside loops.");
            }

            Consume(TokenType.Semicolon, "Expected ';' after 'continue'.");
            return new ContinueStatement();
        }

        private ReturnStatement ParseReturnStatement()
        {
            if (_coroutineFunctionDepth > 0 && !Check(TokenType.Semicolon))
            {
                throw Error(Peek(), "Coroutine functions cannot return a value. Use 'yield return' or 'yield break'.");
            }

            Expression value = null;
            bool hasExpression = !Check(TokenType.Semicolon);

            if (hasExpression)
            {
                value = ParseExpression();
            }

            Consume(TokenType.Semicolon, "Expected ';' after 'return'.");
            return new ReturnStatement(value, hasExpression);
        }

        private YieldStatement ParseYieldStatement()
        {
            if (_coroutineFunctionDepth == 0)
            {
                throw Error(Previous(), "'yield' can only be used inside functions returning IEnumerator.");
            }

            if (Match(TokenType.Break))
            {
                Consume(TokenType.Semicolon, "Expected ';' after 'yield break'.");
                return YieldStatement.CreateBreak();
            }

            if (Match(TokenType.Return))
            {
                Expression value = null;

                if (!Check(TokenType.Semicolon))
                {
                    value = ParseExpression();
                }

                Consume(TokenType.Semicolon, "Expected ';' after 'yield return'.");
                return YieldStatement.CreateReturn(value);
            }

            throw Error(Peek(), "Expected 'return' or 'break' after 'yield'.");
        }

        private FunctionDeclarationStatement ParseFunctionDeclaration()
        {
            string returnTypeName;
            Type returnType = ParseTypeName(out returnTypeName, "Expected function return type.");

            if (returnType == null)
            {
                throw Error(Previous(), $"Unknown type '{returnTypeName}'.");
            }

            Token name = Consume(TokenType.Identifier, "Expected function name.");
            Consume(TokenType.LeftParen, "Expected '(' after function name.");

            var parameters = new List<ScriptFunctionParameter>();

            if (!Check(TokenType.RightParen))
            {
                do
                {
                    string parameterTypeName;
                    Type parameterType = ParseTypeName(out parameterTypeName, "Expected parameter type.");

                    if (parameterType == null)
                    {
                        throw Error(Previous(), $"Unknown type '{parameterTypeName}'.");
                    }

                    if (parameterType == typeof(void))
                    {
                        throw Error(Previous(), "Function parameters cannot have type 'void'.");
                    }

                    Token parameterName = Consume(TokenType.Identifier, "Expected parameter name.");
                    parameters.Add(new ScriptFunctionParameter(parameterName.Lexeme, parameterType));
                }
                while (Match(TokenType.Comma));
            }

            Consume(TokenType.RightParen, "Expected ')' after parameter list.");
            bool isCoroutineFunction = IsCoroutineReturnType(returnType);
            Statement body;

            if (isCoroutineFunction)
            {
                _coroutineFunctionDepth++;
            }

            try
            {
                body = ParseStatement();
            }
            finally
            {
                if (isCoroutineFunction)
                {
                    _coroutineFunctionDepth--;
                }
            }

            return new FunctionDeclarationStatement(name.Lexeme, returnType, parameters, body);
        }

        private static bool IsCoroutineReturnType(Type type)
        {
            return type != null && typeof(System.Collections.IEnumerator).IsAssignableFrom(type);
        }

        private List<Statement> ParseBlockStatements()
        {
            var statements = new List<Statement>();

            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                statements.Add(ParseStatement());
            }

            Consume(TokenType.RightBrace, "Expected '}' after block.");
            return statements;
        }

        private VariableDeclarationStatement ParseVariableDeclaration(bool consumeSemicolon)
        {
            bool inferType = Match(TokenType.Var);
            Type declaredType = null;

            if (!inferType)
            {
                string typeName;
                declaredType = ParseTypeName(out typeName, "Expected variable type.");

                if (declaredType == null)
                {
                    throw Error(Previous(), $"Unknown type '{typeName}'.");
                }
            }

            Token name = Consume(TokenType.Identifier, "Expected variable name.");

            Expression initializer = null;

            if (Match(TokenType.Equal))
            {
                initializer = ParseExpression();
            }

            if (inferType && initializer == null)
            {
                throw Error(name, "A 'var' declaration requires an initializer.");
            }

            if (consumeSemicolon)
            {
                Consume(TokenType.Semicolon, "Expected ';' after variable declaration.");
            }

            return new VariableDeclarationStatement(name, declaredType, inferType, initializer);
        }

        private Statement ParseExpressionStatement()
        {
            Expression expression = ParseExpression();
            Consume(TokenType.Semicolon, "Expected ';' after expression.");
            return new ExpressionStatement(expression);
        }

        private Expression ParseExpression()
        {
            if (TryParseLambdaExpression(out Expression lambdaExpression))
            {
                return lambdaExpression;
            }

            return ParseAssignment();
        }

        private bool TryParseLambdaExpression(out Expression expression)
        {
            expression = null;

            if (!Check(TokenType.LeftParen))
            {
                return false;
            }

            int savedCurrent = _current;
            Advance();
            var parameterNames = new List<string>();

            if (!Check(TokenType.RightParen))
            {
                while (true)
                {
                    if (!Check(TokenType.Identifier))
                    {
                        _current = savedCurrent;
                        return false;
                    }

                    parameterNames.Add(Advance().Lexeme);

                    if (Match(TokenType.Comma))
                    {
                        continue;
                    }

                    break;
                }
            }

            if (!Match(TokenType.RightParen) || !Match(TokenType.Arrow))
            {
                _current = savedCurrent;
                return false;
            }

            Expression body = ParseExpression();
            expression = new LambdaExpression(parameterNames, body);
            return true;
        }

        private Expression ParseAssignment()
        {
            Expression expression;

            if (TryParseLambdaExpression(out Expression lambdaExpression))
            {
                expression = lambdaExpression;
            }
            else
            {
                expression = ParseNullCoalescing();
            }

            if (Match(TokenType.Equal))
            {
                Token equals = Previous();
                Expression value = ParseAssignment();

                if (expression is IAssignableExpression assignable)
                {
                    return new AssignmentExpression(assignable, value);
                }

                throw Error(equals, "Invalid assignment target.");
            }

            if (Match(TokenType.PlusEqual, TokenType.MinusEqual, TokenType.StarEqual, TokenType.SlashEqual, TokenType.PercentEqual))
            {
                Token compoundOperator = Previous();
                Expression value = ParseAssignment();

                if (expression is IAssignableExpression assignable)
                {
                    return new CompoundAssignmentExpression(assignable, compoundOperator, value);
                }

                throw Error(compoundOperator, "Invalid assignment target.");
            }

            return expression;
        }

        private Expression ParseNullCoalescing()
        {
            Expression expression = ParseOr();

            while (Match(TokenType.QuestionQuestion))
            {
                Expression right = ParseOr();
                expression = new NullCoalescingExpression(expression, right);
            }

            return expression;
        }

        private Expression ParseOr()
        {
            Expression expression = ParseAnd();

            while (Match(TokenType.OrOr))
            {
                Token op = Previous();
                Expression right = ParseAnd();
                expression = new BinaryExpression(expression, op, right);
            }

            return expression;
        }

        private Expression ParseAnd()
        {
            Expression expression = ParseEquality();

            while (Match(TokenType.AndAnd))
            {
                Token op = Previous();
                Expression right = ParseEquality();
                expression = new BinaryExpression(expression, op, right);
            }

            return expression;
        }

        private Expression ParseEquality()
        {
            Expression expression = ParseComparison();

            while (Match(TokenType.BangEqual, TokenType.EqualEqual))
            {
                Token op = Previous();
                Expression right = ParseComparison();
                expression = new BinaryExpression(expression, op, right);
            }

            return expression;
        }

        private Expression ParseComparison()
        {
            Expression expression = ParseTerm();

            while (true)
            {
                if (Match(TokenType.Greater, TokenType.GreaterEqual, TokenType.Less, TokenType.LessEqual))
                {
                    Token op = Previous();
                    Expression right = ParseTerm();
                    expression = new BinaryExpression(expression, op, right);
                    continue;
                }

                if (Match(TokenType.Is))
                {
                    string typeName;
                    Type targetType = ParseTypeName(out typeName, "Expected type after 'is'.");

                    if (targetType == null)
                    {
                        throw Error(Previous(), $"Unknown type '{typeName}'.");
                    }

                    expression = new IsExpression(expression, targetType);
                    continue;
                }

                if (Match(TokenType.As))
                {
                    string typeName;
                    Type targetType = ParseTypeName(out typeName, "Expected type after 'as'.");

                    if (targetType == null)
                    {
                        throw Error(Previous(), $"Unknown type '{typeName}'.");
                    }

                    expression = new AsExpression(expression, targetType);
                    continue;
                }

                break;
            }

            return expression;
        }

        private Expression ParseTerm()
        {
            Expression expression = ParseFactor();

            while (Match(TokenType.Plus, TokenType.Minus))
            {
                Token op = Previous();
                Expression right = ParseFactor();
                expression = new BinaryExpression(expression, op, right);
            }

            return expression;
        }

        private Expression ParseFactor()
        {
            Expression expression = ParseUnary();

            while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
            {
                Token op = Previous();
                Expression right = ParseUnary();
                expression = new BinaryExpression(expression, op, right);
            }

            return expression;
        }

        private Expression ParseUnary()
        {
            if (IsExplicitCastStart())
            {
                Consume(TokenType.LeftParen, "Expected '(' before cast type.");
                string typeName;
                Type targetType = ParseTypeName(out typeName, "Expected cast target type.");
                Consume(TokenType.RightParen, "Expected ')' after cast type.");

                if (targetType == null)
                {
                    throw Error(Previous(), $"Unknown type '{typeName}'.");
                }

                Expression operand = ParseUnary();
                return new CastExpression(targetType, operand);
            }

            if (Match(TokenType.Bang, TokenType.Minus))
            {
                Token op = Previous();
                Expression right = ParseUnary();
                return new UnaryExpression(op, right);
            }

            if (Match(TokenType.PlusPlus, TokenType.MinusMinus))
            {
                Token op = Previous();
                Expression target = ParseUnary();

                if (target is IAssignableExpression assignable)
                {
                    return new UpdateExpression(assignable, op, isPrefix: true);
                }

                throw Error(op, "Increment and decrement require an assignable target.");
            }

            return ParsePostfix();
        }

        private Expression ParsePostfix()
        {
            Expression expression = ParseCallAndMemberAccess();

            while (Match(TokenType.PlusPlus, TokenType.MinusMinus))
            {
                Token op = Previous();

                if (expression is IAssignableExpression assignable)
                {
                    expression = new UpdateExpression(assignable, op, isPrefix: false);
                }
                else
                {
                    throw Error(op, "Increment and decrement require an assignable target.");
                }
            }

            return expression;
        }

        private bool IsExplicitCastStart()
        {
            if (!Check(TokenType.LeftParen))
            {
                return false;
            }

            int savedCurrent = _current;
            Advance();
            bool isType = TryReadTypeReference(_current, out _, out int endIndex, out _, out _);
            _current = savedCurrent;

            return isType &&
                   endIndex < _tokens.Count &&
                   _tokens[endIndex].Type == TokenType.RightParen &&
                   endIndex + 1 < _tokens.Count &&
                   IsCastOperandStart(_tokens[endIndex + 1].Type);
        }

        private static bool IsCastOperandStart(TokenType tokenType)
        {
            switch (tokenType)
            {
                case TokenType.LeftParen:
                case TokenType.LeftBracket:
                case TokenType.Identifier:
                case TokenType.Number:
                case TokenType.String:
                case TokenType.InterpolatedString:
                case TokenType.True:
                case TokenType.False:
                case TokenType.Null:
                case TokenType.New:
                case TokenType.Bang:
                case TokenType.Minus:
                case TokenType.PlusPlus:
                case TokenType.MinusMinus:
                    return true;

                default:
                    return false;
            }
        }

        private Expression ParseCallAndMemberAccess()
        {
            Expression expression = ParsePrimary();

            while (true)
            {
                if (Match(TokenType.Dot))
                {
                    Token name = Consume(TokenType.Identifier, "Expected member name after '.'.");

                    if (TryParseGenericMemberCall(expression, name, out Expression genericCallExpression))
                    {
                        expression = genericCallExpression;
                    }
                    else
                    {
                        expression = new MemberExpression(expression, name);
                    }
                }
                else if (Match(TokenType.QuestionDot))
                {
                    Token name = Consume(TokenType.Identifier, "Expected member name after '?.'.");

                    if (TryParseNullConditionalGenericMemberCall(expression, name, out Expression genericNullConditionalCall))
                    {
                        expression = genericNullConditionalCall;
                    }
                    else if (Match(TokenType.LeftParen))
                    {
                        List<Expression> arguments = ParseArgumentsAfterOpeningParenthesis();
                        expression = new NullConditionalCallExpression(expression, name, arguments);
                    }
                    else
                    {
                        expression = new NullConditionalMemberExpression(expression, name);
                    }
                }
                else if (Match(TokenType.LeftParen))
                {
                    List<Expression> arguments = ParseArgumentsAfterOpeningParenthesis();
                    expression = new CallExpression(expression, arguments);
                }
                else if (Match(TokenType.Question))
                {
                    Consume(TokenType.LeftBracket, "Expected '[' after '?' for null-conditional index access.");
                    Expression index = ParseExpression();
                    Consume(TokenType.RightBracket, "Expected ']' after index expression.");
                    expression = new NullConditionalIndexExpression(expression, index);
                }
                else if (Match(TokenType.LeftBracket))
                {
                    Expression index = ParseExpression();
                    Consume(TokenType.RightBracket, "Expected ']' after index expression.");
                    expression = new IndexExpression(expression, index);
                }
                else
                {
                    break;
                }
            }

            return expression;
        }

        private bool TryParseGenericMemberCall(Expression target, Token memberName, out Expression expression)
        {
            expression = null;
            int savedCurrent = _current;

            if (!TryParseGenericTypeArgumentList(out Type[] genericTypeArguments) || !Match(TokenType.LeftParen))
            {
                _current = savedCurrent;
                return false;
            }

            List<Expression> arguments = ParseArgumentsAfterOpeningParenthesis();
            expression = new CallExpression(new MemberExpression(target, memberName, genericTypeArguments), arguments);
            return true;
        }

        private bool TryParseNullConditionalGenericMemberCall(Expression target, Token memberName, out Expression expression)
        {
            expression = null;
            int savedCurrent = _current;

            if (!TryParseGenericTypeArgumentList(out Type[] genericTypeArguments) || !Match(TokenType.LeftParen))
            {
                _current = savedCurrent;
                return false;
            }

            List<Expression> arguments = ParseArgumentsAfterOpeningParenthesis();
            expression = new NullConditionalCallExpression(target, memberName, arguments, genericTypeArguments);
            return true;
        }

        private bool TryParseGenericTypeArgumentList(out Type[] genericTypeArguments)
        {
            genericTypeArguments = null;

            if (!Match(TokenType.Less))
            {
                return false;
            }

            var resolvedTypeArguments = new List<Type>();

            do
            {
                int startIndex = _current;

                if (!TryReadTypeReference(startIndex, out Type resolvedType, out int endIndex, out _, out _))
                {
                    return false;
                }

                resolvedTypeArguments.Add(resolvedType);
                _current = endIndex;
            }
            while (Match(TokenType.Comma));

            if (!Match(TokenType.Greater))
            {
                return false;
            }

            genericTypeArguments = resolvedTypeArguments.ToArray();
            return true;
        }

        private Expression ParsePrimary()
        {
            if (Match(TokenType.False))
            {
                return new LiteralExpression(false);
            }

            if (Match(TokenType.True))
            {
                return new LiteralExpression(true);
            }

            if (Match(TokenType.Null))
            {
                return new LiteralExpression(null);
            }

            if (Match(TokenType.Number, TokenType.String))
            {
                return new LiteralExpression(Previous().Literal);
            }

            if (Match(TokenType.InterpolatedString))
            {
                return new InterpolatedStringExpression((InterpolatedStringTemplate)Previous().Literal, _resolveType);
            }

            if (Match(TokenType.New))
            {
                return ParseNewExpression();
            }

            if (Match(TokenType.Typeof))
            {
                return ParseTypeOfExpression();
            }

            if (Match(TokenType.LeftBracket))
            {
                return ParseArrayLiteralExpression();
            }

            if (TryParseTypeExpression(out Expression typeExpression))
            {
                return typeExpression;
            }

            if (Match(TokenType.Identifier))
            {
                return new VariableExpression(Previous());
            }

            if (Match(TokenType.LeftParen))
            {
                Expression expression = ParseExpression();
                Consume(TokenType.RightParen, "Expected ')' after expression.");
                return new GroupingExpression(expression);
            }

            throw Error(Peek(), "Expected expression.");
        }

        private NewExpression ParseNewExpression()
        {
            string typeName;
            Type type = ParseTypeName(out typeName, "Expected type name after 'new'.");

            if (type == null)
            {
                throw Error(Previous(), $"Unknown type '{typeName}'.");
            }

            if (Match(TokenType.LeftBracket))
            {
                Expression arrayLength = ParseExpression();
                Consume(TokenType.RightBracket, "Expected ']' after array length.");
                return new NewExpression(typeName, type, arrayLength);
            }

            Consume(TokenType.LeftParen, "Expected '(' after constructor type.");
            List<Expression> arguments = ParseArgumentsAfterOpeningParenthesis();

            return new NewExpression(typeName, type, arguments);
        }

        private ArrayLiteralExpression ParseArrayLiteralExpression()
        {
            var elements = new List<Expression>();

            if (!Check(TokenType.RightBracket))
            {
                do
                {
                    elements.Add(ParseExpression());
                }
                while (Match(TokenType.Comma));
            }

            Consume(TokenType.RightBracket, "Expected ']' after array literal.");
            return new ArrayLiteralExpression(elements);
        }

        private TypeExpression ParseTypeOfExpression()
        {
            Token typeofToken = Previous();
            Consume(TokenType.LeftParen, "Expected '(' after 'typeof'.");

            string typeName;
            Type type = ParseTypeName(out typeName, "Expected type name inside 'typeof'.");
            Consume(TokenType.RightParen, "Expected ')' after typeof type.");

            if (type == null)
            {
                throw Error(Previous(), $"Unknown type '{typeName}'.");
            }

            return new TypeExpression(typeofToken, type);
        }

        private List<Expression> ParseArgumentsAfterOpeningParenthesis()
        {
            var arguments = new List<Expression>();

            if (!Check(TokenType.RightParen))
            {
                do
                {
                    arguments.Add(ParseExpression());
                }
                while (Match(TokenType.Comma));
            }

            Consume(TokenType.RightParen, "Expected ')' after argument list.");
            return arguments;
        }

        private bool TryParseTypeExpression(out Expression expression)
        {
            expression = null;

            if (!TryReadTypeReference(_current, out Type type, out int endIndex, out Token firstToken, out _) ||
                endIndex >= _tokens.Count ||
                _tokens[endIndex].Type != TokenType.Dot)
            {
                return false;
            }

            _current = endIndex;
            expression = new TypeExpression(firstToken, type);
            return true;
        }

        private Type ParseTypeName(out string typeName, string errorMessage)
        {
            int startIndex = _current;

            if (!TryReadTypeReference(startIndex, out Type type, out int endIndex, out _, out typeName))
            {
                throw Error(Peek(), errorMessage);
            }

            _current = endIndex;
            return type;
        }

        private bool IsVariableDeclarationStart()
        {
            if (Check(TokenType.Var))
            {
                return true;
            }

            return TryReadTypeReference(_current, out _, out int endIndex, out _, out _) &&
                   endIndex < _tokens.Count &&
                   _tokens[endIndex].Type == TokenType.Identifier;
        }

        private bool IsFunctionDeclarationStart()
        {
            if (!TryReadTypeReference(_current, out _, out int endIndex, out _, out _))
            {
                return false;
            }

            return endIndex + 1 < _tokens.Count &&
                   _tokens[endIndex].Type == TokenType.Identifier &&
                   _tokens[endIndex + 1].Type == TokenType.LeftParen;
        }

        private bool TryReadTypeReference(
            int startIndex,
            out Type type,
            out int endIndex,
            out Token firstToken,
            out string typeName)
        {
            type = null;
            endIndex = startIndex;
            firstToken = default;
            typeName = null;

            if (startIndex >= _tokens.Count || _tokens[startIndex].Type != TokenType.Identifier)
            {
                return false;
            }

            firstToken = _tokens[startIndex];
            List<string> segments = ParseQualifiedTypeSegments(startIndex, out List<int> positionsAfterSegment);

            if (segments == null || segments.Count == 0)
            {
                return false;
            }

            for (int prefixLength = segments.Count; prefixLength >= 1; prefixLength--)
            {
                string baseTypeName = string.Join(".", segments.GetRange(0, prefixLength));
                int cursor = positionsAfterSegment[prefixLength - 1];

                List<Type> genericArguments = null;
                var displayName = new StringBuilder(baseTypeName);

                if (cursor < _tokens.Count && _tokens[cursor].Type == TokenType.Less)
                {
                    cursor++;
                    genericArguments = new List<Type>();
                    displayName.Append('<');

                    while (true)
                    {
                        if (!TryReadTypeReference(cursor, out Type genericType, out int genericEndIndex, out _, out string genericTypeName))
                        {
                            genericArguments = null;
                            break;
                        }

                        genericArguments.Add(genericType);
                        displayName.Append(genericTypeName);
                        cursor = genericEndIndex;

                        if (cursor < _tokens.Count && _tokens[cursor].Type == TokenType.Comma)
                        {
                            displayName.Append(", ");
                            cursor++;
                            continue;
                        }

                        break;
                    }

                    if (genericArguments == null)
                    {
                        continue;
                    }

                    if (cursor >= _tokens.Count || _tokens[cursor].Type != TokenType.Greater)
                    {
                        continue;
                    }

                    displayName.Append('>');
                    cursor++;
                }

                Type resolvedType = ResolveParsedType(baseTypeName, genericArguments);

                if (resolvedType == null)
                {
                    continue;
                }

                while (cursor + 1 < _tokens.Count &&
                       _tokens[cursor].Type == TokenType.LeftBracket &&
                       _tokens[cursor + 1].Type == TokenType.RightBracket)
                {
                    resolvedType = resolvedType.MakeArrayType();
                    displayName.Append("[]");
                    cursor += 2;
                }

                type = resolvedType;
                typeName = displayName.ToString();
                endIndex = cursor;
                return true;
            }

            return false;
        }

        private List<string> ParseQualifiedTypeSegments(int startIndex, out List<int> positionsAfterSegment)
        {
            positionsAfterSegment = new List<int>();

            if (startIndex >= _tokens.Count || _tokens[startIndex].Type != TokenType.Identifier)
            {
                return null;
            }

            var segments = new List<string>();
            int cursor = startIndex;

            segments.Add(_tokens[cursor].Lexeme);
            cursor++;
            positionsAfterSegment.Add(cursor);

            while (cursor + 1 < _tokens.Count &&
                   _tokens[cursor].Type == TokenType.Dot &&
                   _tokens[cursor + 1].Type == TokenType.Identifier)
            {
                segments.Add(_tokens[cursor + 1].Lexeme);
                cursor += 2;
                positionsAfterSegment.Add(cursor);
            }

            return segments;
        }

        private Type ResolveParsedType(string baseTypeName, List<Type> genericArguments)
        {
            if (genericArguments == null || genericArguments.Count == 0)
            {
                return _resolveType(baseTypeName);
            }

            Type genericDefinition = _resolveType($"{baseTypeName}`{genericArguments.Count}");

            if (genericDefinition == null)
            {
                genericDefinition = _resolveType(baseTypeName);
            }

            if (genericDefinition == null)
            {
                return null;
            }

            if (!genericDefinition.IsGenericTypeDefinition ||
                genericDefinition.GetGenericArguments().Length != genericArguments.Count)
            {
                return null;
            }

            try
            {
                return genericDefinition.MakeGenericType(genericArguments.ToArray());
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private bool Match(params TokenType[] types)
        {
            foreach (TokenType type in types)
            {
                if (Check(type))
                {
                    Advance();
                    return true;
                }
            }

            return false;
        }

        private bool Check(TokenType type)
        {
            return !IsAtEnd() && Peek().Type == type;
        }

        private Token Advance()
        {
            if (!IsAtEnd())
            {
                _current++;
            }

            return Previous();
        }

        private bool IsAtEnd()
        {
            return Peek().Type == TokenType.EndOfFile;
        }

        private Token Peek()
        {
            return _tokens[_current];
        }

        private Token Previous()
        {
            return _tokens[_current - 1];
        }

        private Token Consume(TokenType type, string message)
        {
            if (Check(type))
            {
                return Advance();
            }

            throw Error(Peek(), message);
        }

        private ScriptException Error(Token token, string message)
        {
            return new ScriptException($"Parser error at line {token.Line}, column {token.Column}: {message}");
        }

        internal static Expression ParseInlineExpression(string source, Func<string, Type> resolveType)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var tokens = new Lexer(source).ScanTokens();
            var parser = new Parser(tokens, resolveType);
            Expression expression = parser.ParseExpression();

            if (!parser.IsAtEnd())
            {
                throw parser.Error(parser.Peek(), "Expected end of interpolation expression.");
            }

            return expression;
        }
    }
}

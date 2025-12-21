using System;
using System.Collections.Generic;

namespace LegendaryTools.GenericExpressionEngine
{
    /// <summary>
    /// Simple tokenizer for expressions with identifiers, numbers, booleans and operators.
    /// Supports:
    /// - Numeric literals (0, 1.23)
    /// - Identifiers (variables and function names), including variables prefixed with '$'
    /// - Boolean literals: true, false
    /// - Relational operators: >, <, >=, <=, ==, !=
    /// - Logical operators: and, or, not, &&, ||, !
    /// - Assignment '=', semicolon ';'
    /// </summary>
    internal sealed class Tokenizer
    {
        private readonly string _text;
        private int _position;

        public Tokenizer(string text)
        {
            _text = text ?? string.Empty;
            _position = 0;
        }

        public List<Token> Tokenize()
        {
            List<Token> tokens = new();

            while (true)
            {
                SkipWhiteSpace();
                if (IsEnd())
                {
                    tokens.Add(new Token(TokenKind.EndOfInput, string.Empty));
                    break;
                }

                char c = Peek();

                // IMPORTANT: numbers start ONLY with digits,
                // not with '.' by itself.
                if (char.IsDigit(c))
                    tokens.Add(ReadNumber());
                else if (char.IsLetter(c) || c == '_' || c == '$')
                    tokens.Add(ReadIdentifierOrKeyword());
                else
                    switch (c)
                    {
                        case '+':
                            tokens.Add(new Token(TokenKind.Plus, c.ToString()));
                            Advance();
                            break;
                        case '-':
                            tokens.Add(new Token(TokenKind.Minus, c.ToString()));
                            Advance();
                            break;
                        case '*':
                            tokens.Add(new Token(TokenKind.Star, c.ToString()));
                            Advance();
                            break;
                        case '/':
                            tokens.Add(new Token(TokenKind.Slash, c.ToString()));
                            Advance();
                            break;
                        case '^':
                            tokens.Add(new Token(TokenKind.Caret, c.ToString()));
                            Advance();
                            break;
                        case '(':
                            tokens.Add(new Token(TokenKind.LParen, c.ToString()));
                            Advance();
                            break;
                        case ')':
                            tokens.Add(new Token(TokenKind.RParen, c.ToString()));
                            Advance();
                            break;
                        case ',':
                            tokens.Add(new Token(TokenKind.Comma, c.ToString()));
                            Advance();
                            break;
                        case ';':
                            tokens.Add(new Token(TokenKind.Semicolon, c.ToString()));
                            Advance();
                            break;
                        case '=':
                            if (PeekNext() == '=')
                            {
                                tokens.Add(new Token(TokenKind.EqualEqual, "=="));
                                Advance(2);
                            }
                            else
                            {
                                tokens.Add(new Token(TokenKind.Equals, c.ToString()));
                                Advance();
                            }

                            break;
                        case '>':
                            if (PeekNext() == '=')
                            {
                                tokens.Add(new Token(TokenKind.GreaterOrEqual, ">="));
                                Advance(2);
                            }
                            else
                            {
                                tokens.Add(new Token(TokenKind.Greater, c.ToString()));
                                Advance();
                            }

                            break;
                        case '<':
                            if (PeekNext() == '=')
                            {
                                tokens.Add(new Token(TokenKind.LessOrEqual, "<="));
                                Advance(2);
                            }
                            else
                            {
                                tokens.Add(new Token(TokenKind.Less, c.ToString()));
                                Advance();
                            }

                            break;
                        case '!':
                            if (PeekNext() == '=')
                            {
                                tokens.Add(new Token(TokenKind.BangEqual, "!="));
                                Advance(2);
                            }
                            else
                            {
                                tokens.Add(new Token(TokenKind.Not, "!"));
                                Advance();
                            }

                            break;
                        case '&':
                            if (PeekNext() == '&')
                            {
                                tokens.Add(new Token(TokenKind.And, "&&"));
                                Advance(2);
                            }
                            else
                            {
                                throw new InvalidOperationException(
                                    $"Unexpected character '&' at position {_position}. Use '&&' for logical AND.");
                            }

                            break;
                        case '|':
                            if (PeekNext() == '|')
                            {
                                tokens.Add(new Token(TokenKind.Or, "||"));
                                Advance(2);
                            }
                            else
                            {
                                throw new InvalidOperationException(
                                    $"Unexpected character '|' at position {_position}. Use '||' for logical OR.");
                            }

                            break;
                        case '.':
                            // Scope separator: player.$hp, self.parent.$hp
                            tokens.Add(new Token(TokenKind.Dot, "."));
                            Advance();
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Unexpected character '{c}' at position {_position}.");
                    }
            }

            return tokens;
        }

        private void SkipWhiteSpace()
        {
            while (!IsEnd() && char.IsWhiteSpace(Peek()))
            {
                Advance();
            }
        }

        private bool IsEnd()
        {
            return _position >= _text.Length;
        }

        private char Peek()
        {
            return _text[_position];
        }

        private char PeekNext()
        {
            int index = _position + 1;
            return index < _text.Length ? _text[index] : '\0';
        }

        private void Advance()
        {
            _position++;
        }

        private void Advance(int count)
        {
            _position += count;
            if (_position > _text.Length) _position = _text.Length;
        }

        private Token ReadNumber()
        {
            int start = _position;
            bool hasDecimalSeparator = false;

            while (!IsEnd())
            {
                char c = Peek();
                if (char.IsDigit(c))
                {
                    Advance();
                }
                else if (c == '.' && !hasDecimalSeparator)
                {
                    // decimal separator inside the number, e.g. 3.14
                    hasDecimalSeparator = true;
                    Advance();
                }
                else
                {
                    break;
                }
            }

            string text = _text.Substring(start, _position - start);
            return new Token(TokenKind.Number, text);
        }

        private Token ReadIdentifierOrKeyword()
        {
            int start = _position;

            Advance(); // first char already letter, '_', or '$'

            while (!IsEnd())
            {
                char c = Peek();
                if (char.IsLetterOrDigit(c) || c == '_')
                    Advance();
                else
                    break;
            }

            string text = _text.Substring(start, _position - start);

            // Keywords apply only if the identifier does not start with '$'
            if (!string.IsNullOrEmpty(text) && text[0] != '$')
            {
                string lower = text.ToLowerInvariant();
                switch (lower)
                {
                    case "and":
                        return new Token(TokenKind.And, text);
                    case "or":
                        return new Token(TokenKind.Or, text);
                    case "not":
                        return new Token(TokenKind.Not, text);
                    case "true":
                        return new Token(TokenKind.True, text);
                    case "false":
                        return new Token(TokenKind.False, text);
                }
            }

            return new Token(TokenKind.Identifier, text);
        }
    }
}
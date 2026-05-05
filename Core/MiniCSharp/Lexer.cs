using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LegendaryTools.MiniCSharp
{
    internal sealed class Lexer
    {
        private readonly string _source;
        private readonly List<Token> _tokens = new List<Token>();
        private int _start;
        private int _current;
        private int _line = 1;
        private int _column = 1;
        private int _tokenColumn = 1;

        private static readonly Dictionary<string, TokenType> Keywords = new Dictionary<string, TokenType>
        {
            { "true", TokenType.True },
            { "false", TokenType.False },
            { "null", TokenType.Null },
            { "if", TokenType.If },
            { "else", TokenType.Else },
            { "for", TokenType.For },
            { "while", TokenType.While },
            { "break", TokenType.Break },
            { "continue", TokenType.Continue },
            { "return", TokenType.Return },
            { "var", TokenType.Var },
            { "new", TokenType.New }
        };

        public Lexer(string source)
        {
            _source = source;
        }

        public List<Token> ScanTokens()
        {
            while (!IsAtEnd())
            {
                _start = _current;
                _tokenColumn = _column;
                ScanToken();
            }

            _tokens.Add(new Token(TokenType.EndOfFile, string.Empty, null, _line, _column));
            return _tokens;
        }

        private void ScanToken()
        {
            char c = Advance();

            switch (c)
            {
                case '(':
                    AddToken(TokenType.LeftParen);
                    break;

                case ')':
                    AddToken(TokenType.RightParen);
                    break;

                case '[':
                    AddToken(TokenType.LeftBracket);
                    break;

                case ']':
                    AddToken(TokenType.RightBracket);
                    break;

                case '{':
                    AddToken(TokenType.LeftBrace);
                    break;

                case '}':
                    AddToken(TokenType.RightBrace);
                    break;

                case ';':
                    AddToken(TokenType.Semicolon);
                    break;

                case ',':
                    AddToken(TokenType.Comma);
                    break;

                case '.':
                    AddToken(TokenType.Dot);
                    break;

                case '+':
                    if (Match('+'))
                    {
                        AddToken(TokenType.PlusPlus);
                    }
                    else if (Match('='))
                    {
                        AddToken(TokenType.PlusEqual);
                    }
                    else
                    {
                        AddToken(TokenType.Plus);
                    }
                    break;

                case '-':
                    if (Match('-'))
                    {
                        AddToken(TokenType.MinusMinus);
                    }
                    else if (Match('='))
                    {
                        AddToken(TokenType.MinusEqual);
                    }
                    else
                    {
                        AddToken(TokenType.Minus);
                    }
                    break;

                case '*':
                    AddToken(Match('=') ? TokenType.StarEqual : TokenType.Star);
                    break;

                case '%':
                    AddToken(Match('=') ? TokenType.PercentEqual : TokenType.Percent);
                    break;

                case '!':
                    AddToken(Match('=') ? TokenType.BangEqual : TokenType.Bang);
                    break;

                case '=':
                    AddToken(Match('=') ? TokenType.EqualEqual : TokenType.Equal);
                    break;

                case '<':
                    AddToken(Match('=') ? TokenType.LessEqual : TokenType.Less);
                    break;

                case '>':
                    AddToken(Match('=') ? TokenType.GreaterEqual : TokenType.Greater);
                    break;

                case '&':
                    if (Match('&'))
                    {
                        AddToken(TokenType.AndAnd);
                        break;
                    }

                    throw Error("Unexpected character '&'. Use '&&' for logical AND.");

                case '|':
                    if (Match('|'))
                    {
                        AddToken(TokenType.OrOr);
                        break;
                    }

                    throw Error("Unexpected character '|'. Use '||' for logical OR.");

                case '/':
                    if (Match('/'))
                    {
                        while (Peek() != '\n' && !IsAtEnd())
                        {
                            Advance();
                        }
                    }
                    else if (Match('*'))
                    {
                        ConsumeBlockComment();
                    }
                    else
                    {
                        AddToken(Match('=') ? TokenType.SlashEqual : TokenType.Slash);
                    }

                    break;

                case ' ':
                case '\r':
                case '\t':
                    break;

                case '\n':
                    _line++;
                    _column = 1;
                    break;

                case '"':
                    ScanString();
                    break;

                default:
                    if (IsDigit(c))
                    {
                        ScanNumber();
                    }
                    else if (IsIdentifierStart(c))
                    {
                        ScanIdentifier();
                    }
                    else
                    {
                        throw Error($"Unexpected character '{c}'.");
                    }

                    break;
            }
        }

        private void ConsumeBlockComment()
        {
            while (!IsAtEnd())
            {
                if (Peek() == '*' && PeekNext() == '/')
                {
                    Advance();
                    Advance();
                    return;
                }

                if (Peek() == '\n')
                {
                    Advance();
                    _line++;
                    _column = 1;
                }
                else
                {
                    Advance();
                }
            }

            throw Error("Unterminated block comment.");
        }

        private void ScanString()
        {
            var value = new StringBuilder();

            while (!IsAtEnd())
            {
                char c = Advance();

                if (c == '"')
                {
                    AddToken(TokenType.String, value.ToString());
                    return;
                }

                if (c == '\\')
                {
                    if (IsAtEnd())
                    {
                        throw Error("Unterminated string escape.");
                    }

                    char escaped = Advance();

                    switch (escaped)
                    {
                        case 'n':
                            value.Append('\n');
                            break;

                        case 'r':
                            value.Append('\r');
                            break;

                        case 't':
                            value.Append('\t');
                            break;

                        case '\\':
                            value.Append('\\');
                            break;

                        case '"':
                            value.Append('"');
                            break;

                        default:
                            throw Error($"Unsupported string escape '\\{escaped}'.");
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        _line++;
                        _column = 1;
                    }

                    value.Append(c);
                }
            }

            throw Error("Unterminated string.");
        }

        private void ScanNumber()
        {
            while (IsDigit(Peek()))
            {
                Advance();
            }

            bool hasDot = false;

            if (Peek() == '.' && IsDigit(PeekNext()))
            {
                hasDot = true;
                Advance();

                while (IsDigit(Peek()))
                {
                    Advance();
                }
            }

            bool isFloatSuffix = false;

            if (Peek() == 'f' || Peek() == 'F')
            {
                isFloatSuffix = true;
                Advance();
            }

            string text = _source.Substring(_start, _current - _start);

            if (hasDot || isFloatSuffix)
            {
                string normalized = text.TrimEnd('f', 'F');
                float value = float.Parse(normalized, CultureInfo.InvariantCulture);
                AddToken(TokenType.Number, value);
            }
            else
            {
                int value = int.Parse(text, CultureInfo.InvariantCulture);
                AddToken(TokenType.Number, value);
            }
        }

        private void ScanIdentifier()
        {
            while (IsIdentifierPart(Peek()))
            {
                Advance();
            }

            string text = _source.Substring(_start, _current - _start);

            if (Keywords.TryGetValue(text, out TokenType keyword))
            {
                AddToken(keyword);
            }
            else
            {
                AddToken(TokenType.Identifier);
            }
        }

        private char Advance()
        {
            char c = _source[_current++];
            _column++;
            return c;
        }

        private bool Match(char expected)
        {
            if (IsAtEnd() || _source[_current] != expected)
            {
                return false;
            }

            _current++;
            _column++;
            return true;
        }

        private char Peek()
        {
            return IsAtEnd() ? '\0' : _source[_current];
        }

        private char PeekNext()
        {
            return _current + 1 >= _source.Length ? '\0' : _source[_current + 1];
        }

        private bool IsAtEnd()
        {
            return _current >= _source.Length;
        }

        private void AddToken(TokenType type)
        {
            AddToken(type, null);
        }

        private void AddToken(TokenType type, object literal)
        {
            string text = _source.Substring(_start, _current - _start);
            _tokens.Add(new Token(type, text, literal, _line, _tokenColumn));
        }

        private ScriptException Error(string message)
        {
            return new ScriptException($"Lexer error at line {_line}, column {_tokenColumn}: {message}");
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static bool IsIdentifierStart(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        private static bool IsIdentifierPart(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }
    }
}

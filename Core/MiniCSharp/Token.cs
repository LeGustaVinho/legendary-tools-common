namespace LegendaryTools.MiniCSharp
{
    internal readonly struct Token
    {
        public Token(TokenType type, string lexeme, object literal, int line, int column)
        {
            Type = type;
            Lexeme = lexeme;
            Literal = literal;
            Line = line;
            Column = column;
        }

        public TokenType Type { get; }

        public string Lexeme { get; }

        public object Literal { get; }

        public int Line { get; }

        public int Column { get; }

        public override string ToString()
        {
            return $"{Type} '{Lexeme}' at {Line}:{Column}";
        }
    }
}
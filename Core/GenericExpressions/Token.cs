namespace LegendaryTools.GenericExpressionEngine
{
    internal readonly struct Token
    {
        public TokenKind Kind { get; }
        public string Text { get; }

        public Token(TokenKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        public override string ToString()
        {
            return $"{Kind}: {Text}";
        }
    }
}
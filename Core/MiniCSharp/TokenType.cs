namespace LegendaryTools.MiniCSharp
{
    internal enum TokenType
    {
        LeftParen,
        RightParen,
        LeftBracket,
        RightBracket,
        LeftBrace,
        RightBrace,
        Semicolon,
        Comma,
        Dot,

        Plus,
        PlusEqual,
        Minus,
        MinusEqual,
        Star,
        StarEqual,
        Slash,
        SlashEqual,
        Percent,
        PercentEqual,

        Bang,
        BangEqual,
        Equal,
        EqualEqual,
        Greater,
        GreaterEqual,
        Less,
        LessEqual,

        AndAnd,
        OrOr,
        PlusPlus,
        MinusMinus,

        Identifier,
        Number,
        String,

        True,
        False,
        Null,

        If,
        Else,
        For,
        While,
        Break,
        Continue,
        Return,
        Var,
        New,

        EndOfFile
    }
}

internal enum TokenKind
{
    Number,
    Identifier,

    Plus,
    Minus,
    Star,
    Slash,
    Caret,
    LParen,
    RParen,
    Comma,
    Semicolon,
    Equals,

    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    EqualEqual,
    BangEqual,

    And,
    Or,
    Not,

    True,
    False,

    Dot,

    EndOfInput
}
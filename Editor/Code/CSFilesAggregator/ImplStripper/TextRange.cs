namespace CSharpRegexStripper
{
    public readonly struct TextRange
    {
        public int Start { get; }
        public int Length { get; }
        public int End => Start + Length;

        public TextRange(int start, int length)
        {
            Start = start;
            Length = length < 0 ? 0 : length;
        }

        public bool Intersects(int start, int length)
        {
            int end = start + (length < 0 ? 0 : length);
            return start < End && end > Start;
        }

        public bool Contains(int index) => index >= Start && index < End;
    }
}

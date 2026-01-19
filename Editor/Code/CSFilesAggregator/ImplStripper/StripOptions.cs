using System;

namespace CSharpRegexStripper
{
    public readonly struct StripOptions : IEquatable<StripOptions>
    {
        public MethodBodyMode MethodBodyMode { get; }
        public bool ConvertNonAutoGetSetPropertiesToAutoProperties { get; }
        public bool MaskStringsAndCommentsBeforeStripping { get; }
        public bool SkipInterfaceMembers { get; }
        public bool SkipAbstractMembers { get; }

        public StripOptions(
            MethodBodyMode methodBodyMode,
            bool convertNonAutoGetSetPropertiesToAutoProperties,
            bool maskStringsAndCommentsBeforeStripping,
            bool skipInterfaceMembers,
            bool skipAbstractMembers)
        {
            MethodBodyMode = methodBodyMode;
            ConvertNonAutoGetSetPropertiesToAutoProperties = convertNonAutoGetSetPropertiesToAutoProperties;
            MaskStringsAndCommentsBeforeStripping = maskStringsAndCommentsBeforeStripping;
            SkipInterfaceMembers = skipInterfaceMembers;
            SkipAbstractMembers = skipAbstractMembers;
        }

        public static StripOptions Default =>
            new(
                MethodBodyMode.Semicolon,
                true,
                true,
                true,
                true
            );

        public StripOptions WithSkipInterfaceMembers(bool value)
        {
            return new StripOptions(
                MethodBodyMode,
                ConvertNonAutoGetSetPropertiesToAutoProperties,
                MaskStringsAndCommentsBeforeStripping,
                value,
                SkipAbstractMembers);
        }

        public StripOptions WithSkipAbstractMembers(bool value)
        {
            return new StripOptions(
                MethodBodyMode,
                ConvertNonAutoGetSetPropertiesToAutoProperties,
                MaskStringsAndCommentsBeforeStripping,
                SkipInterfaceMembers,
                value);
        }

        public bool Equals(StripOptions other)
        {
            return MethodBodyMode == other.MethodBodyMode &&
                   ConvertNonAutoGetSetPropertiesToAutoProperties ==
                   other.ConvertNonAutoGetSetPropertiesToAutoProperties &&
                   MaskStringsAndCommentsBeforeStripping == other.MaskStringsAndCommentsBeforeStripping &&
                   SkipInterfaceMembers == other.SkipInterfaceMembers &&
                   SkipAbstractMembers == other.SkipAbstractMembers;
        }

        public override bool Equals(object obj)
        {
            return obj is StripOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)MethodBodyMode;
                hash = (hash * 397) ^ (ConvertNonAutoGetSetPropertiesToAutoProperties ? 1 : 0);
                hash = (hash * 397) ^ (MaskStringsAndCommentsBeforeStripping ? 1 : 0);
                hash = (hash * 397) ^ (SkipInterfaceMembers ? 1 : 0);
                hash = (hash * 397) ^ (SkipAbstractMembers ? 1 : 0);
                return hash;
            }
        }

        public static bool operator ==(StripOptions left, StripOptions right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StripOptions left, StripOptions right)
        {
            return !left.Equals(right);
        }
    }
}
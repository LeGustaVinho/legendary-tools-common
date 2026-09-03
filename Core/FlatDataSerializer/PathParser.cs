using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlatData
{
    internal enum PathTokenType
    {
        Member,
        Index
    }

    internal struct PathToken
    {
        public PathToken(string memberName)
        {
            Type = PathTokenType.Member;
            MemberName = memberName;
            Index = -1;
        }

        public PathToken(int index)
        {
            Type = PathTokenType.Index;
            MemberName = null;
            Index = index;
        }

        public PathTokenType Type { get; }

        public string MemberName { get; }

        public int Index { get; }
    }

    internal static class PathParser
    {
        public static IReadOnlyList<PathToken> Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));
            }

            List<PathToken> tokens = new List<PathToken>();
            StringBuilder member = new StringBuilder();

            for (int index = 0; index < path.Length; index++)
            {
                char character = path[index];

                if (character == '.')
                {
                    FlushMember(member, tokens, path);
                    continue;
                }

                if (character == '[')
                {
                    FlushMember(member, tokens, path);

                    int closingBracket = path.IndexOf(']', index + 1);
                    if (closingBracket < 0)
                    {
                        throw new FormatException(
                            $"Missing closing bracket in path '{path}'.");
                    }

                    string indexText = path.Substring(
                        index + 1,
                        closingBracket - index - 1);

                    int collectionIndex;
                    if (!int.TryParse(
                        indexText,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out collectionIndex) ||
                        collectionIndex < 0)
                    {
                        throw new FormatException(
                            $"Invalid collection index '{indexText}' in path '{path}'.");
                    }

                    tokens.Add(new PathToken(collectionIndex));
                    index = closingBracket;
                    continue;
                }

                member.Append(character);
            }

            FlushMember(member, tokens, path);
            return tokens;
        }

        private static void FlushMember(
            StringBuilder member,
            ICollection<PathToken> tokens,
            string path)
        {
            if (member.Length == 0)
            {
                return;
            }

            string memberName = member.ToString();
            if (string.IsNullOrWhiteSpace(memberName))
            {
                throw new FormatException($"Invalid member in path '{path}'.");
            }

            tokens.Add(new PathToken(memberName));
            member.Length = 0;
        }
    }
}

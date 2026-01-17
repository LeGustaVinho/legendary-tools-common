using System.Text.RegularExpressions;

namespace CSharpRegexStripper
{
    public static class CSharpMasking
    {
        private static readonly Regex MaskTargets = new Regex(
            @"(?sx)
                (//[^\r\n]*)
              | (/\*.*?\*/)
              | (?:@""(?:""""|[^""])*"")
              | (?:""(?:\\.|[^""\\])*"")
              | (?:'(?:\\.|[^'\\])')
            ",
            RegexOptions.Compiled
        );

        public static string MaskStringsAndComments(string source)
        {
            if (source == null)
                return null;

            return MaskTargets.Replace(source, m => new string(' ', m.Length));
        }
    }
}

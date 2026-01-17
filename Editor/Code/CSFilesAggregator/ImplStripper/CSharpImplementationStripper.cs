using System;
using System.IO;
using System.Text;

namespace CSharpRegexStripper
{
    public static class CSharpImplementationStripper
    {
        public static string StripFromString(string source)
        {
            return StripFromString(source, StripOptions.Default);
        }

        public static string StripFromString(string source, StripOptions options)
        {
            if (source == null)
                return null;

            string masked = options.MaskStringsAndCommentsBeforeStripping
                ? CSharpMasking.MaskStringsAndComments(source)
                : source;

            string result = source;

            if (options.ConvertNonAutoGetSetPropertiesToAutoProperties)
            {
                result = CSharpRewriters.ConvertNonAutoGetSetPropertiesToAutoProperties(result, masked, options);
                masked = options.MaskStringsAndCommentsBeforeStripping
                    ? CSharpMasking.MaskStringsAndComments(result)
                    : result;
            }

            result = CSharpRewriters.StripMethodBodies(result, masked, options);

            return result;
        }

        public static string StripFromFile(string filePath)
        {
            return StripFromFile(filePath, StripOptions.Default, null);
        }

        public static string StripFromFile(string filePath, StripOptions options)
        {
            return StripFromFile(filePath, options, null);
        }

        public static string StripFromFile(string filePath, StripOptions options, Encoding encoding)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is null or empty.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found.", filePath);

            string source = encoding == null
                ? File.ReadAllText(filePath)
                : File.ReadAllText(filePath, encoding);

            return StripFromString(source, options);
        }

        public static void StripFileInPlace(string filePath)
        {
            StripFileInPlace(filePath, StripOptions.Default, null);
        }

        public static void StripFileInPlace(string filePath, StripOptions options)
        {
            StripFileInPlace(filePath, options, null);
        }

        public static void StripFileInPlace(string filePath, StripOptions options, Encoding encoding)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is null or empty.", nameof(filePath));

            string stripped = StripFromFile(filePath, options, encoding);

            if (encoding == null)
                File.WriteAllText(filePath, stripped);
            else
                File.WriteAllText(filePath, stripped, encoding);
        }
    }
}

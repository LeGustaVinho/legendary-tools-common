using System;
using System.Collections.Generic;
using System.Text;

namespace LegendaryTools
{
    public static class StringExtension
    {
        public static string CamelCaseToAllUpperWithUnderscores(this string text)
        {
            text = text.Replace(" ", "");
            List<char> characters = new List<char>();

            characters.AddRange(text.ToCharArray());

            for (int i = characters.Count - 1; i >= 0; i--)
            {
                if (Char.IsUpper(characters[i]) && i != 0)
                {
                    characters.Insert(i, '_');
                }

                characters[i] = Char.ToUpper(characters[i]);
            }

            return new string(characters.ToArray());
        }
        
        public static string FilterEnumName(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return "_";

            StringBuilder sb = new StringBuilder();
            int position = 0;

            // Handle the first character
            char firstChar = input[0];
            if (char.IsLetter(firstChar) || firstChar == '_')
            {
                sb.Append(firstChar);
                position = 1;
            }
            else
            {
                sb.Append('_');
            }

            // Process the remaining characters
            for (int i = position; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
                // Ignore invalid characters
            }

            return sb.ToString();
        }
    }
}
using System;
using System.Collections.Generic;

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
    }
}
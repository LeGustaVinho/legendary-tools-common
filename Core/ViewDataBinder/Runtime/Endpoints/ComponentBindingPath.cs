using System;
using System.Text;
using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public static class ComponentBindingPath
    {
        private const string Prefix = "$component|";
        private const string MemberSeparator = "::";
        private static readonly Dictionary<string, string> DisplayTypeNameCache =
            new Dictionary<string, string>();

        public static string Create(Type componentType, int typeOrdinal, string memberPath)
        {
            if (componentType == null)
            {
                throw new ArgumentNullException(nameof(componentType));
            }

            string encodedType = Encode(componentType.AssemblyQualifiedName);
            return $"{Prefix}{encodedType}|{typeOrdinal}{MemberSeparator}{memberPath}";
        }

        public static bool TryParse(
            string path,
            out string componentTypeName,
            out int typeOrdinal,
            out string memberPath)
        {
            componentTypeName = null;
            typeOrdinal = -1;
            memberPath = null;

            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            int separatorIndex = path.IndexOf(MemberSeparator, Prefix.Length, StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                return false;
            }

            string header = path.Substring(Prefix.Length, separatorIndex - Prefix.Length);
            int ordinalSeparatorIndex = header.LastIndexOf('|');
            if (ordinalSeparatorIndex <= 0 || ordinalSeparatorIndex >= header.Length - 1)
            {
                return false;
            }

            string encodedType = header.Substring(0, ordinalSeparatorIndex);
            string ordinalText = header.Substring(ordinalSeparatorIndex + 1);

            if (!int.TryParse(ordinalText, out typeOrdinal) || typeOrdinal < 0)
            {
                typeOrdinal = -1;
                return false;
            }

            try
            {
                componentTypeName = Decode(encodedType);
            }
            catch (FormatException)
            {
                componentTypeName = null;
                typeOrdinal = -1;
                return false;
            }

            memberPath = path.Substring(separatorIndex + MemberSeparator.Length);
            return !string.IsNullOrWhiteSpace(componentTypeName) &&
                   !string.IsNullOrWhiteSpace(memberPath);
        }

        public static string GetDisplayPath(string path)
        {
            if (!TryParse(path, out string typeName, out int typeOrdinal, out string memberPath))
            {
                return path;
            }

            if (!DisplayTypeNameCache.TryGetValue(typeName, out string typeDisplayName))
            {
                Type type = DefaultBindingInstanceResolver.FindType(typeName);
                typeDisplayName = type?.Name ?? "Component";
                DisplayTypeNameCache[typeName] = typeDisplayName;
            }
            string ordinalSuffix = typeOrdinal > 0 ? $" [{typeOrdinal + 1}]" : string.Empty;
            return $"{typeDisplayName}{ordinalSuffix}.{memberPath}";
        }

        private static string Encode(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string Decode(string value)
        {
            string base64 = value
                .Replace('-', '+')
                .Replace('_', '/');

            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
    }
}

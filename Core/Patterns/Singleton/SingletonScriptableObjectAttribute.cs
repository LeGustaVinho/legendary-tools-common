using System;

namespace LegendaryTools
{
    [AttributeUsage(AttributeTargets.Class)]
    public class SingletonScriptableObjectAttribute : Attribute
    {
        private static string DEFAULT_PATH = "Assets/Resources";
        private static char KEYPAD_DIVIDE_CHAR = '/';
        private static string KEYPAD_DIVIDE_STRING = "/";
        private static char SCAPED_BACKSLASH = '\\';
        private static string RESOURCES_FOLDER = "/Resources/";
        private static string ASSETS_FOLDER = "Assets/";

        private string assetPath;

        public string AssetPath
        {
            get
            {
                string path = assetPath
                    .Trim()
                    .TrimEnd(KEYPAD_DIVIDE_CHAR, SCAPED_BACKSLASH)
                    .TrimStart(KEYPAD_DIVIDE_CHAR, SCAPED_BACKSLASH)
                    .Replace(SCAPED_BACKSLASH, KEYPAD_DIVIDE_CHAR) + KEYPAD_DIVIDE_STRING;
                return path;
            }
        }

        public string AssetPathWithAssetsPrefix
        {
            get
            {
                string path = AssetPath;
                if (path.StartsWith(ASSETS_FOLDER)) return path;
                return ASSETS_FOLDER + path;
            }
        }

        public string AssetPathWithoutAssetsPrefix
        {
            get
            {
                string path = AssetPath;
                if (path.StartsWith(ASSETS_FOLDER)) return path.Substring(ASSETS_FOLDER.Length);
                return path;
            }
        }

        public bool UseAsset = true;

        public bool IsInResourcesFolder => AssetPath.IndexOf(RESOURCES_FOLDER, StringComparison.OrdinalIgnoreCase) > 0;

        public string ResourcesPath
        {
            get
            {
                if (IsInResourcesFolder)
                {
                    string resourcesPath = AssetPath;
                    int i = resourcesPath.LastIndexOf(RESOURCES_FOLDER, StringComparison.InvariantCultureIgnoreCase);
                    if (i >= 0) return resourcesPath.Substring(i + RESOURCES_FOLDER.Length);
                }

                return string.Empty;
            }
        }

        public SingletonScriptableObjectAttribute() : this(DEFAULT_PATH)
        {
        }

        public SingletonScriptableObjectAttribute(string assetPath)
        {
            this.assetPath = assetPath;
        }
    }
}
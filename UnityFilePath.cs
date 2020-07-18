using System;
using UnityEngine;

namespace LegendaryTools
{
    public enum UnityFilePathType
    {
        Data,
        Persistent,
        StreamingAssets,
        TemporaryCache
    }

    [Serializable]
    public struct UnityFilePath
    {
        private const string FILENAME_WITH_EXTENSION_FORMAT = "{0}.{1}";
        private const string FILEPATH_FORMAT1 = "{0}/{1}/{2}";
        private const string FILEPATH_FORMAT2 = "{0}/{1}";
        private const string DEFAULT_UNNAMED_FILE = "UNNAMED_FILE";

        public bool UseBackwardsSlash;
        public UnityFilePathType RootPathType;
        public string PostRootPath;
        public string FileName;
        public string Extension;

        public string Path =>
            string.IsNullOrEmpty(PostRootPath)
                ? FormatSlashs(string.Format(FILEPATH_FORMAT2, ResolvedRootPathType, ResolvedFileName))
                : FormatSlashs(string.Format(FILEPATH_FORMAT1, ResolvedRootPathType, PostRootPath, ResolvedFileName));

        private string ResolvedRootPathType
        {
            get
            {
                switch (RootPathType)
                {
                    case UnityFilePathType.Data: return Application.dataPath;
                    case UnityFilePathType.Persistent: return Application.persistentDataPath;
                    case UnityFilePathType.StreamingAssets: return Application.streamingAssetsPath;
                    case UnityFilePathType.TemporaryCache: return Application.temporaryCachePath;
                }

                return string.Empty;
            }
        }

        private string ResolvedFileName => string.IsNullOrEmpty(Extension)
            ? FileName
            : string.Format(FILENAME_WITH_EXTENSION_FORMAT,
                string.IsNullOrEmpty(FileName) ? DEFAULT_UNNAMED_FILE : FileName, Extension);

        private string FormatSlashs(string path)
        {
            if (UseBackwardsSlash)
            {
                return path.Replace('/', '\\');
            }

            return path.Replace('\\', '/');
        }
    }
}
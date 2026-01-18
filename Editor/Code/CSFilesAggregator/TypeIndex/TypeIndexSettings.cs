// Assets/legendary-tools-common/Editor/Code/CSFilesAggregator/TypeIndex/TypeIndexSettings.cs
using System.IO;
using UnityEditor;

namespace LegendaryTools.CSFilesAggregator.TypeIndex
{
    /// <summary>
    /// Editor settings for configuring which roots are scanned when building the type index.
    /// </summary>
    [FilePath("ProjectSettings/LegendaryToolsTypeIndexSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class TypeIndexSettings : ScriptableSingleton<TypeIndexSettings>
    {
        /// <summary>
        /// Gets or sets a value indicating whether the <c>Assets</c> folder should be scanned.
        /// </summary>
        public bool ScanAssets = true;

        /// <summary>
        /// Gets or sets a value indicating whether the <c>Packages</c> folder should be scanned.
        /// </summary>
        public bool ScanPackages = true;

        /// <summary>
        /// Gets or sets a value indicating whether <c>Library/PackageCache</c> should be scanned.
        /// </summary>
        public bool ScanPackageCache = false;

        /// <summary>
        /// Gets or sets a value indicating whether files under hidden folders (starting with '.') should be ignored.
        /// </summary>
        public bool IgnoreHiddenFolders = true;

        /// <summary>
        /// Gets the absolute project root path.
        /// </summary>
        public string GetProjectRootAbsolute()
        {
            // Application.dataPath points to ".../<Project>/Assets".
            string assetsPath = UnityEngine.Application.dataPath;
            return Path.GetFullPath(Path.Combine(assetsPath, ".."));
        }

        /// <summary>
        /// Saves the settings asset.
        /// </summary>
        public void Save()
        {
            Save(true);
        }
    }
}

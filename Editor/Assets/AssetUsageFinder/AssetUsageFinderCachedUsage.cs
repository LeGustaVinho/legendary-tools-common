using Newtonsoft.Json;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public sealed class AssetUsageFinderCachedUsage
    {
        public bool IsComponent { get; set; }

        [JsonIgnore] public Object Reference { get; set; }

        public string DisplayName { get; set; }
        public string HierarchyPath { get; set; }
        public bool GameObjectActive { get; set; }
        public bool? ComponentEnabled { get; set; }
        public string ComponentTypeAssemblyQualifiedName { get; set; }

        public AssetUsageFinderCachedUsage()
        {
        }

        public AssetUsageFinderCachedUsage(
            bool isComponent,
            Object reference,
            string displayName,
            string hierarchyPath,
            bool gameObjectActive,
            bool? componentEnabled,
            string componentTypeAssemblyQualifiedName = null)
        {
            IsComponent = isComponent;
            Reference = reference;
            DisplayName = displayName;
            HierarchyPath = hierarchyPath;
            GameObjectActive = gameObjectActive;
            ComponentEnabled = componentEnabled;
            ComponentTypeAssemblyQualifiedName = componentTypeAssemblyQualifiedName;
        }
    }
}
namespace LegendaryTools
{
    public class ScriptableObjectInfoAttribute : System.Attribute
    {
        public string DisplayName { get; }
        public string Description { get; }
        public string HierarchyPath { get; }

        public ScriptableObjectInfoAttribute(string displayName, string hierarchyPath = "Default",
            string description = "")
        {
            DisplayName = displayName;
            HierarchyPath = hierarchyPath;
            Description = description;
        }
    }
}
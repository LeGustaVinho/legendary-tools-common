using UnityEditor;
using UnityEngine;

namespace HierarchyDecorator
{
    [System.Serializable]
    public class BranchCategoryThemeColors
    {
        public Color twoD;
        public Color ui;
        public Color threeD;

        public BranchCategoryThemeColors(Color twoD, Color ui, Color threeD)
        {
            this.twoD = twoD;
            this.ui = ui;
            this.threeD = threeD;
        }
    }

    [System.Serializable]
    public class BranchCategoryColorSettings
    {
        public BranchCategoryThemeColors darkMode = new BranchCategoryThemeColors(
            new Color(0.38f, 0.86f, 0.67f, 1f),
            new Color(1.00f, 0.79f, 0.38f, 1f),
            new Color(0.80f, 0.70f, 0.98f, 1f));

        public BranchCategoryThemeColors lightMode = new BranchCategoryThemeColors(
            new Color(0.13f, 0.50f, 0.35f, 1f),
            new Color(0.72f, 0.41f, 0.10f, 1f),
            new Color(0.41f, 0.25f, 0.72f, 1f));

        public void EnsureInitialized()
        {
            if (darkMode == null)
            {
                darkMode = new BranchCategoryThemeColors(
                    new Color(0.38f, 0.86f, 0.67f, 1f),
                    new Color(1.00f, 0.79f, 0.38f, 1f),
                    new Color(0.80f, 0.70f, 0.98f, 1f));
            }

            if (lightMode == null)
            {
                lightMode = new BranchCategoryThemeColors(
                    new Color(0.13f, 0.50f, 0.35f, 1f),
                    new Color(0.72f, 0.41f, 0.10f, 1f),
                    new Color(0.41f, 0.25f, 0.72f, 1f));
            }
        }
    }

    /// <summary>
    /// User-specific settings stored under the project's Library folder.
    /// </summary>
    [FilePath("Library/HierarchyDecorator/Settings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class Settings : ScriptableSingleton<Settings>, ISerializationCallbackReceiver
    {
        public bool enableHierarchyDecorator = true;
        public bool enableBranchCategoryColorDrawer = true;
        public GlobalData globalData = new GlobalData();
        public HierarchyStyleData styleData = new HierarchyStyleData();
        public BranchCategoryColorSettings branchCategoryColors = new BranchCategoryColorSettings();

        [SerializeField]
        private ComponentData components = new ComponentData();

        public ComponentData Components
        {
            get
            {
                return components;
            }
        }

        private void OnEnable()
        {
            EnsureInitialized();
            components.OnInitialize();
        }

        internal void SetDefaults(bool isDarkMode)
        {
            EnsureInitialized();
            components.UpdateData();
            styleData.UpdateStyles(isDarkMode);
        }

        public void OnBeforeSerialize()
        {
            EnsureInitialized();
            components.UpdateData();
        }

        public void OnAfterDeserialize()
        {
            EnsureInitialized();
            components.UpdateData();
        }

        public void SaveSettings()
        {
            Save(true);
        }

        internal void EnsureInitialized()
        {
            if (globalData == null)
            {
                globalData = new GlobalData();
            }

            if (styleData == null)
            {
                styleData = new HierarchyStyleData();
            }

            if (branchCategoryColors == null)
            {
                branchCategoryColors = new BranchCategoryColorSettings();
            }

            branchCategoryColors.EnsureInitialized();

            if (components == null)
            {
                components = new ComponentData();
            }
        }
    }
}

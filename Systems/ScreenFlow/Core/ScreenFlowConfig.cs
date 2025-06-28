using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LegendaryTools.Systems.ScreenFlow
{
    [System.Serializable]
    public struct PlatformBackKeyConfig
    {
        public RuntimePlatform Platform;
        public KeyCode BackKey;
        public bool Enabled;
    }

    [CreateAssetMenu(menuName = "Tools/ScreenFlow/ScreenFlowConfig")]
    public class ScreenFlowConfig : ScriptableObject, IWeaveExec
    {
#if ODIN_INSPECTOR
        [HideInInspector]
#endif
        [SerializeField] private WeaveExecType weaveExecType;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public WeaveExecType WeaveExecType
        {
            get => weaveExecType;
            set => weaveExecType = value;
        }

        public ScreenConfig[] Screens;
        public PopupConfig[] Popups;
        public Canvas OverridePopupCanvasPrefab;

        public ScreenConfig StartScreen;

        /// <summary>
        /// Configuration for platform-specific back key settings.
        /// </summary>
        public PlatformBackKeyConfig[] PlatformBackKeys = new[]
        {
            new PlatformBackKeyConfig()
            {
                Platform = RuntimePlatform.WindowsEditor,
                BackKey = KeyCode.Escape,
                Enabled = true
            },
            new PlatformBackKeyConfig()
            {
                Platform = RuntimePlatform.Android,
                BackKey = KeyCode.Escape,
                Enabled = true
            },
            new PlatformBackKeyConfig()
            {
                Platform = RuntimePlatform.OSXEditor,
                BackKey = KeyCode.Escape,
                Enabled = true
            },
            new PlatformBackKeyConfig()
            {
                Platform = RuntimePlatform.WindowsPlayer,
                BackKey = KeyCode.Escape,
                Enabled = true
            },
            new PlatformBackKeyConfig()
            {
                Platform = RuntimePlatform.OSXPlayer,
                BackKey = KeyCode.Escape,
                Enabled = true
            }
        };

        [Header("Debug")]
        public bool Verbose;
        
#if UNITY_EDITOR
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
#endif
        public void FindConfigs()
        {
            Screens = this.FindAssetConfigNear<ScreenConfig>().ToArray();
            Popups = this.FindAssetConfigNear<PopupConfig>().ToArray();
            EditorUtility.SetDirty(this);
        }
#endif
        public void RunWeaver()
        {
#if UNITY_EDITOR
            FindConfigs();
#endif
        }
    }
}
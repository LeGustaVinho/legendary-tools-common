using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LegendaryTools.Systems.ScreenFlow
{
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
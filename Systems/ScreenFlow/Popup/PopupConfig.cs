using UnityEngine;

namespace LegendaryTools.Systems.ScreenFlow
{
    [CreateAssetMenu(menuName = "Tools/ScreenFlow/PopupConfig")]
    public class PopupConfig : UIEntityBaseConfig
    {
        public PopupGoingBackgroundBehaviour GoingBackgroundBehaviour = PopupGoingBackgroundBehaviour.DontHide;
    }
}
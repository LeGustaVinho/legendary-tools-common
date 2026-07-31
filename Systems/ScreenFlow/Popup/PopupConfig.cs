using UnityEngine;

namespace LegendaryTools.Systems.ScreenFlow
{
    [CreateAssetMenu(menuName = "Legendary Tools/Screen Flow/Configuration/Popup")]
    public class PopupConfig : UIEntityBaseConfig
    {
        public PopupGoingBackgroundBehaviour GoingBackgroundBehaviour = PopupGoingBackgroundBehaviour.DontHide;
    }
}

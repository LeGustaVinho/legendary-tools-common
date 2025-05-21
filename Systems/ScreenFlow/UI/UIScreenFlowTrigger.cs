using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.Systems.ScreenFlow
{
    public class UIScreenFlowTrigger : MonoBehaviour
    {
        public ScreenFlowTriggerMode Mode = ScreenFlowTriggerMode.Trigger;
        public UIEntityBaseConfig UiEntity;
        public bool Enqueue;
        
        private Button button;

        public virtual IScreenFlow ScreenFlowInstance
        {
#if SCREEN_FLOW_SINGLETON
            get => ScreenFlow.Instance;
#else
            get
            {
                Debug.LogWarning($"[{nameof(UIScreenFlowTrigger)}:{nameof(ScreenFlowInstance)}] Cannot be executed because ScreenFlow is not singleton, define SCREEN_FLOW_SINGLETON or override this property.");
                return null;
            }
#endif
        }
        
        public void ProcessTrigger()
        {
            if (ScreenFlowInstance != null)
            {
                switch (Mode)
                {
                    case ScreenFlowTriggerMode.Trigger:
                    {
                        ScreenFlowInstance.SendTrigger(UiEntity, enqueue:Enqueue);
                        break;
                    }
                    case ScreenFlowTriggerMode.MoveBack:
                    {
                        ScreenFlowInstance.MoveBack(enqueue:Enqueue);
                        break;
                    }
                    case ScreenFlowTriggerMode.ClosePopup:
                    {
                        ScreenFlowInstance.CloseForegroundPopup(enqueue:Enqueue);
                        break;
                    }
                }
            }
        }

        private void Start()
        {
            button = GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(ProcessTrigger);
            }
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(ProcessTrigger);
            }
        }
    }
}

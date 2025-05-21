using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace LegendaryTools.UI
{
    [Serializable]
    public class OnDropFinishUnityEvent : UnityEvent<UIDrag>
    {
    }

    public class UIDrop : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("Slot can hold only one item, otherwise will replace to new item")]
        public bool Exclusive;

        public OnDropFinishUnityEvent OnDropFinishHandler;

        [Tooltip("Parent the object with slot(container)")]
        public bool ParentWithContainer;

        [Tooltip("Restore object original anchors and pivot")]
        public bool RestoreAnchorAndPivot;

        [Tooltip("Snap object using uGUI anchoringPosition. (Do no stack with another Snap)")]
        public bool SnapAnchoring = true;

        [Tooltip("Snap object using transformPosition. (Do no stack with another Snap)")]
        public bool SnapTransformPosition;

        [Tooltip("Current item stored in slot")]
        public UIDrag Stored;

        [Tooltip("Swap objects if slot are occupied")]
        public bool SwapItems;

        public void OnDrop(PointerEventData eventData)
        {
            if (!UIDrag.Dragging[eventData.pointerId].OnEndDrag)
            {
                UIDrag.Dragging[eventData.pointerId].Object.GetComponent<UIDrag>()
                    .OnEndDrag(eventData); //Ensure execution order OnBeginDrag -> OnDrag -> OnEndDrag -> OnDrop
            }

            if (UIDrag.Dragging[eventData.pointerId].OnDrop)
            {
                return;
            }

            //Debug.Log("OnDrop " + eventData.pointerId);

            UIDrag.Dragging[eventData.pointerId].Object.SnapTo(this);

            if (Stored != null)
            {
                Destroy(Stored.gameObject); //remove old to replace to new object
                Stored = null;
            }

            Stored = UIDrag.Dragging[eventData.pointerId].Object;
            UIDrag.Dragging[eventData.pointerId].Object.CurrentContainer = this;

            OnDropFinishHandler.Invoke(UIDrag.Dragging[eventData.pointerId].Object); //alias to OnDropFinish
            OnDropFinish(UIDrag.Dragging[eventData.pointerId].Object); //alias to OnDropFinishHandler

            UIDrag.Dragging[eventData.pointerId].OnDrop = true;
            //UIDrag.Dragging[eventData.pointerId] = null;
        }

        public virtual void OnPointerEnter(PointerEventData data)
        {
        }

        public virtual void OnPointerExit(PointerEventData data)
        {
        }

        protected virtual void OnDropFinish(UIDrag draggedObject)
        {
        }

        public virtual void OnHoverStart()
        {
        }

        public virtual void OnHoverEnd()
        {
        }

        //Override this if you want a custom drop logic
        public virtual bool CanDrop(PointerEventData eventData, UIDrag draggingObject)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(transform as RectTransform, eventData.position,
                eventData.enterEventCamera)) //to make sure that the object will be inside a slot
            {
                if (SwapItems)
                {
                    return true;
                }

                if (Exclusive && Stored != null)
                {
                    return false;
                }

                return true;
            }

            Debug.Log("Refused by cursor location.");
            return false;
        }

        public void DestroyItemStored()
        {
            if (Stored != null)
            {
                Destroy(Stored.gameObject);
                Stored = null;
            }
        }
    }
}
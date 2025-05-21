using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LegendaryTools.UI
{
    public class UIDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public static readonly Dictionary<int, DraggingObject> Dragging = new Dictionary<int, DraggingObject>();
        private readonly List<RaycastResult> hoveredRaycastResults = new List<RaycastResult>();

        protected Vector2 anchoredPosition3D;
        protected Vector2 anchorMax;
        protected Vector2 anchorMin;
        protected Canvas Canvas;
        public CanvasGroup CanvasGroup;

        public UIDrop CurrentContainer;

        [Tooltip("Object will be destroyed if drop in a non-slot. (Do not stack with return)")]
        public bool DestroyOnDropInvalid;

        [Tooltip("Adapt rotation to current slot rotation")]
        public bool DragOnSurfaces = true;

        [Tooltip("Prefab will be duplicated")] public UIDrag DragPrefab;

        [Tooltip("ReScale object in local space to reduce parent scale impact")]
        public bool ForcePrefabScale = true;

        public List<UIDrop> hovered = new List<UIDrop>();

        protected bool IsInit;
        protected Vector2 pivot;

        protected RectTransform RectTransform;

        [Tooltip("Back to the original state if space is not a slot. (Do not stack with destroy)")]
        public bool ReturnoToStart = true;

        [Tooltip(
            "Object will not be returned to start position and rotation. Override OnReturnToStart() to animate it.")]
        public bool ReturnWillBeAnimated;

        protected Vector2 sizeDelta;
        protected Transform startParent;

        protected Vector3 startPosition; //global position
        protected Quaternion startRotation; //local rotation

        [Tooltip("Use Transform.position instead RectTransform.anchoredPosition3D")]
        public bool UseTransformPosition = true;

        public void OnBeginDrag(PointerEventData eventData)
        {
            Init();
            if (IsInit)
            {
                cache();

                if (DragPrefab) //will clone object
                {
                    Dragging[eventData.pointerId] = new DraggingObject(eventData.pointerId,
                        Instantiate(DragPrefab, transform.position, transform.rotation));
                    if (ForcePrefabScale)
                    {
                        Dragging[eventData.pointerId].Object.transform.localScale = DragPrefab.transform.localScale;
                    }
                }
                else //will move yourselfs
                {
                    Dragging[eventData.pointerId] = new DraggingObject(eventData.pointerId, this);
                }

                if (CanvasGroup == null)
                {
                    CanvasGroup = GetComponent<CanvasGroup>();
                }

                if (CanvasGroup == null)
                {
                    CanvasGroup = gameObject.AddComponent<CanvasGroup>();
                }

                CanvasGroup.blocksRaycasts = false;

                Dragging[eventData.pointerId].Object.AnchorAndPivotCenter();
                Dragging[eventData.pointerId].Object.transform.SetParent(Canvas.transform, false);
                Dragging[eventData.pointerId].Object.transform.SetAsLastSibling();

                if (DragOnSurfaces)
                {
                    Dragging[eventData.pointerId].Plane = transform as RectTransform;
                }
                else
                {
                    Dragging[eventData.pointerId].Plane = Canvas.transform as RectTransform;
                }

                setDraggedPosition(eventData);
                OnDragStart(eventData);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            Init();

            if (Dragging[eventData.pointerId] != null)
            {
                setDraggedPosition(eventData);
            }

            checkHover(eventData);

            OnDragUpdate(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Dragging[eventData.pointerId].OnEndDrag)
            {
                return;
            }

            //Debug.Log("OnEndDrag " + eventData.pointerId);

            UIDrop droppedTarget = null;
            foreach (UIDrop targetSlot in hovered)
            {
                //Debug.Log("Hovering " + targetSlot.transform.name);

                if (targetSlot != CurrentContainer && targetSlot.CanDrop(eventData, this)
                ) //prevent self-container swap and drop check
                {
                    UIDrag other = targetSlot.Stored;
                    UIDrop destinationContainer = targetSlot;
                    UIDrop originContainer = CurrentContainer;

                    if (targetSlot.SwapItems)
                    {
                        if (targetSlot.Stored != null)
                        {
                            other.SnapTo(originContainer, true); //move other UIDrag to origin UIDrop
                            other.CurrentContainer = originContainer;
                            other.CanvasGroup.blocksRaycasts = true;
                            originContainer.Stored = other;

                            destinationContainer.Stored = null;
                            CurrentContainer = null;

                            Debug.Log("Swapped");
                            Debug.Log("Other[" + other.name + "] container: " + other.CurrentContainer.name +
                                      " | Origin Container[" + originContainer.name + "]: " +
                                      originContainer.Stored.name);

                            other.OnSlotChanged(destinationContainer, originContainer);
                        }
                        else
                        {
                            if (originContainer != null)
                            {
                                originContainer.Stored = null;
                            }

                            CurrentContainer = null;
                        }
                    }
                    else
                    {
                        if (originContainer != null)
                        {
                            originContainer.Stored = null; //remove reference from container
                        }

                        CurrentContainer = null;
                    }

                    droppedTarget = destinationContainer;
                    OnSlotChanged(originContainer, destinationContainer);
                    break;
                }

                Debug.Log("Valid container[" + targetSlot.name + "] = " + (targetSlot != CurrentContainer) +
                          " | Can be dropped = " + targetSlot.CanDrop(eventData, this));
            }

            if (droppedTarget == null)
            {
                Dragging[eventData.pointerId].OnDrop = true; //prevent OnDrop execution when cant drop
                if (DestroyOnDropInvalid)
                {
                    if (CurrentContainer != null)
                    {
                        CurrentContainer.Stored = null; //remove reference from container
                    }

                    Destroy(gameObject);
                    return;
                }

                if (ReturnoToStart)
                {
                    RestoreAll(ReturnWillBeAnimated); //back to slot position
                }
            }

            forceCleanAllHover();
            CanvasGroup.blocksRaycasts = true;
            OnDragEnd(eventData);
            Dragging[eventData.pointerId].OnEndDrag = true;

            if (droppedTarget != null)
            {
                droppedTarget.OnDrop(eventData);
            }
        }

        public virtual void OnDestroy()
        {
            //clean hovered objects
            for (int i = hovered.Count - 1; i >= 0; i--)
            {
                hovered[i].OnHoverEnd();
                if (hovered[i].Stored != null)
                {
                    if (hovered[i].Stored.CanvasGroup != null)
                    {
                        hovered[i].Stored.CanvasGroup.blocksRaycasts = true;
                    }
                }

                if (CurrentContainer != null)
                {
                    CurrentContainer.Stored = null;
                }

                hovered.RemoveAt(i);
            }
        }

        public void SnapTo(UIDrop slot, bool ensureRotation = false)
        {
            Init();

            if (slot.ParentWithContainer)
            {
                transform.SetParent(slot.transform);
            }

            if (slot.RestoreAnchorAndPivot)
            {
                RestoreAnchorPivot();
            }

            if (slot.SnapAnchoring)
            {
                RectTransform.anchoredPosition3D = (slot.transform as RectTransform).anchoredPosition3D;
            }

            if (slot.SnapTransformPosition)
            {
                transform.position = slot.transform.position;
            }

            if (ensureRotation)
            {
                transform.rotation = slot.transform.rotation;
            }
        }

        protected void Init()
        {
            if (IsInit)
            {
                return;
            }

            CanvasGroup = GetComponent<CanvasGroup>();
            if (CanvasGroup == null)
            {
                CanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            RectTransform = GetComponent<RectTransform>();

            Canvas = GetComponentInParent<Canvas>();
            if (Canvas == null)
            {
                return;
            }

            cache();
            IsInit = true;
        }

        protected void cache()
        {
            startPosition = transform.position;
            startRotation = transform.localRotation;
            startParent = transform.parent;

            anchoredPosition3D = RectTransform.anchoredPosition3D;
            anchorMin = RectTransform.anchorMin;
            anchorMax = RectTransform.anchorMax;
            pivot = RectTransform.pivot;
            sizeDelta = RectTransform.sizeDelta;
        }

        //restore anchor, pivot and size
        protected void RestoreAnchorPivot()
        {
            RectTransform.anchorMin = anchorMin;
            RectTransform.anchorMax = anchorMax;
            RectTransform.pivot = pivot;
            RectTransform.sizeDelta = sizeDelta;
        }

        //move anrchor and pivot to center e keep width and height
        protected void AnchorAndPivotCenter()
        {
            Vector2 newSize = new Vector2(RectTransform.rect.width, RectTransform.rect.height);
            RectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            RectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            RectTransform.pivot = new Vector2(0.5f, 0.5f);
            RectTransform.sizeDelta = newSize;
        }

        //restore all (anchor, pivot, anchorPosition)
        protected void RestoreAll(bool hasAnimation = false)
        {
            RestoreAnchorPivot();
            transform.SetParent(startParent);

            if (!hasAnimation)
            {
                if (UseTransformPosition)
                {
                    transform.position = startPosition;
                    transform.localRotation = startRotation;
                }
                else
                {
                    RectTransform.anchoredPosition3D = anchoredPosition3D; //use anchoredPosition or TransformPosition?
                }
            }
            else
            {
                if (UseTransformPosition)
                {
                    OnReturnToStart(startPosition, startRotation, transform.position, transform.rotation);
                }
                else
                {
                    OnReturnToStart(anchoredPosition3D, startRotation, RectTransform.anchoredPosition3D,
                        transform.rotation);
                }
            }
        }

        protected virtual void OnSlotChanged(UIDrop from, UIDrop to)
        {
        }

        //override this to do returned animation
        protected virtual void OnReturnToStart(Vector3 startPos, Quaternion startRot, Vector3 endPos,
            Quaternion endRotation)
        {
        }

        protected virtual void OnDragUpdate(PointerEventData eventData)
        {
        }

        protected virtual void OnDragStart(PointerEventData eventData)
        {
        }

        protected virtual void OnDragEnd(PointerEventData eventData)
        {
        }

        private void setDraggedPosition(PointerEventData eventData)
        {
            if (DragOnSurfaces && eventData.pointerEnter != null &&
                eventData.pointerEnter.transform as RectTransform != null)
            {
                Dragging[eventData.pointerId].Plane = eventData.pointerEnter.transform as RectTransform;
            }

            RectTransform rt = Dragging[eventData.pointerId].Object.GetComponent<RectTransform>();
            Vector3 globalMousePos;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(Dragging[eventData.pointerId].Plane,
                eventData.position, eventData.pressEventCamera, out globalMousePos))
            {
                rt.position = globalMousePos;
                rt.rotation = Dragging[eventData.pointerId].Plane.rotation;
            }
        }

        private void checkHover(PointerEventData eventData)
        {
            hoveredRaycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, hoveredRaycastResults);
            foreach (RaycastResult result in hoveredRaycastResults)
            {
                UIDrop targetSlot = result.gameObject.GetComponent<UIDrop>();
                if (targetSlot != null)
                {
                    //add new hovered object
                    if (!hovered.Contains(targetSlot))
                    {
                        hovered.Add(targetSlot);
                        targetSlot.OnHoverStart();
                        if (targetSlot.Stored != null)
                        {
                            if (!targetSlot.Stored.IsInit)
                            {
                                targetSlot.Stored.Init();
                            }

                            if (targetSlot.Stored.CanvasGroup != null)
                            {
                                targetSlot.Stored.CanvasGroup.blocksRaycasts = false;
                            }
                        }
                    }
                }
            }

            //clean not hovered old objects
            for (int i = hovered.Count - 1; i >= 0; i--)
            {
                RaycastResult found = hoveredRaycastResults.Find(item => item.gameObject == hovered[i].gameObject);
                if (!found.isValid)
                {
                    hovered[i].OnHoverEnd();
                    if (hovered[i].Stored != null)
                    {
                        if (!hovered[i].Stored.IsInit)
                        {
                            hovered[i].Stored.Init();
                        }

                        if (hovered[i].Stored.CanvasGroup != null)
                        {
                            hovered[i].Stored.CanvasGroup.blocksRaycasts = true;
                        }
                    }

                    hovered.RemoveAt(i);
                }
            }
        }

        private void forceCleanAllHover()
        {
            //clean not hovered old objects
            for (int i = hovered.Count - 1; i >= 0; i--)
            {
                hovered[i].OnHoverEnd();
                if (hovered[i].Stored != null)
                {
                    if (!hovered[i].Stored.IsInit)
                    {
                        hovered[i].Stored.Init();
                    }

                    if (hovered[i].Stored.CanvasGroup != null)
                    {
                        hovered[i].Stored.CanvasGroup.blocksRaycasts = true;
                    }
                }

                hovered.RemoveAt(i);
            }
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace LegendaryTools.UI
{
    public delegate void OnCircularScrollViewChangeEventHandler(GameObject newSelected, GameObject oldSelected);

    [Serializable]
    public class CircularScrollViewChangeEventHandler : UnityEvent<GameObject>
    {
    }

    [Serializable]
    public class ScrollViewItem
    {
        private Vector3[] Corners = new Vector3[4];

        public float DistanceFromCenter;
        public GameObject GameObject;

        public bool IsVisible;

        public Vector2 LastAnchoredPosition;
        public Rect Rect;
        public RectTransform RectTransform;
        public Transform Transform;

        public ScrollViewItem(Transform transform)
        {
            Transform = transform;
            GameObject = transform.gameObject;
            RectTransform = transform.GetComponent<RectTransform>();

            if (RectTransform != null)
            {
                LastAnchoredPosition = RectTransform.anchoredPosition;
            }
        }

        public void UpdateVisibility(Rect parentRect)
        {
            if (RectTransform != null)
            {
                RectTransform.GetWorldCorners(Corners);
                Rect.Set(Corners[1].x, Corners[1].y, Mathf.Abs(Corners[2].x - Corners[1].x),
                    Mathf.Abs(Corners[0].y - Corners[1].y));
                IsVisible = parentRect.Overlaps(Rect);
            }
        }

        public void UpdateDistance(Vector3 center, CircularScrollViewDirection direction)
        {
            if (direction == CircularScrollViewDirection.Horizontal)
            {
                DistanceFromCenter = center.x - Transform.position.x;
            }

            if (direction == CircularScrollViewDirection.Vertical)
            {
                DistanceFromCenter = center.y - Transform.position.y;
            }
        }
    }

    public enum CircularScrollViewDirection
    {
        Horizontal,
        Vertical
    }

    [ExecuteInEditMode]
    public class CircularScrollView : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler,
        IEndDragHandler, IDragHandler, IScrollHandler
    {
        public const float SNAP_DISTANCE_THRESHOLD = 0.1f;
        private readonly List<ScrollViewItem> contentChilds = new List<ScrollViewItem>();
        private readonly Vector3[] corners = new Vector3[4];

        private Vector3 alignBuffer;

        public TextAnchor Alignment;

        private Vector3 center;
        private int childCount;
        private ScrollViewItem closestChild;

        public float DesacelerationRate;

        public CircularScrollViewDirection Direction;

        public Transform FirstSelected;

        [Space] public bool Inertia;

        [HideInInspector] public bool IsDragging;

        private Vector2 moveDelta = Vector2.zero;

        public CircularScrollViewChangeEventHandler OnSelectedChange;
        private Vector2 pointerStartLocalCursor = Vector2.zero;

        private Rect rect = new Rect();
        private float repositionBuffer;

        [Space] public bool SnapAtCenter;

        public float SnapSpeed;

        public float Spacing;

        private Vector2 speed = Vector2.zero;
        private RectTransform thisRectTransform;

        private Transform thisTransform;

        public void OnBeginDrag(PointerEventData eventData)
        {
            pointerStartLocalCursor = Vector2.zero;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(thisRectTransform, eventData.position,
                eventData.pressEventCamera, out pointerStartLocalCursor);
            IsDragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            Vector2 localCursor;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(thisRectTransform, eventData.position,
                eventData.pressEventCamera, out localCursor))
            {
                return;
            }

            moveDelta = localCursor - pointerStartLocalCursor;

            speed = eventData.delta;

            SetContentAnchoredPosition(eventData.delta, moveDelta);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            IsDragging = false;

            if (!Inertia)
            {
                speed = Vector2.zero;
            }
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
            }
        }

        public void OnScroll(PointerEventData eventData)
        {
        }

        public event OnCircularScrollViewChangeEventHandler OnChange;

        protected virtual void SetContentAnchoredPosition(Vector2 deltaPosition, Vector2 moveDelta)
        {
            for (int i = 0; i < contentChilds.Count; i++)
            {
                if (Direction != CircularScrollViewDirection.Horizontal)
                {
                    deltaPosition.x = 0;
                }

                if (Direction != CircularScrollViewDirection.Vertical)
                {
                    deltaPosition.y = 0;
                }

                if (deltaPosition != contentChilds[i].RectTransform.anchoredPosition)
                {
                    contentChilds[i].RectTransform.anchoredPosition += deltaPosition;
                }

                contentChilds[i].UpdateDistance(center, Direction);

                ScrollViewItem aux = null;

                if (closestChild == null)
                {
                    aux = closestChild;
                    closestChild = contentChilds[i];
                    onChange(closestChild.GameObject, aux != null ? aux.GameObject : null);
                }
                else
                {
                    if (Mathf.Abs(contentChilds[i].DistanceFromCenter) < Mathf.Abs(closestChild.DistanceFromCenter))
                    {
                        aux = closestChild;
                        closestChild = contentChilds[i];
                        onChange(closestChild.GameObject, aux != null ? aux.GameObject : null);
                    }
                }
            }
        }

        protected virtual void Awake()
        {
            thisTransform = transform;
            thisRectTransform = thisTransform as RectTransform;
        }

        protected virtual void Start()
        {
            UpdateCache();

            Reposition();
        }

        protected virtual void Update()
        {
            UpdateCache();

            if (!IsDragging)
            {
                if (SnapAtCenter)
                {
                    if (Mathf.Abs(closestChild.DistanceFromCenter) > SNAP_DISTANCE_THRESHOLD)
                    {
                        if (Direction == CircularScrollViewDirection.Horizontal)
                        {
                            speed = Vector3.Lerp(
                                Mathf.Sign(closestChild.DistanceFromCenter) *
                                Mathf.Clamp(Mathf.Abs(closestChild.DistanceFromCenter), 0, SnapSpeed) * Vector2.right,
                                Vector3.zero, Time.deltaTime);
                        }

                        if (Direction == CircularScrollViewDirection.Vertical)
                        {
                            speed = Vector3.Lerp(
                                Mathf.Sign(closestChild.DistanceFromCenter) *
                                Mathf.Clamp(Mathf.Abs(closestChild.DistanceFromCenter), 0, SnapSpeed) * Vector2.up,
                                Vector3.zero, Time.deltaTime);
                        }

                        SetContentAnchoredPosition(speed, Vector2.zero);
                    }
                }

                if (Inertia || SnapAtCenter == false)
                {
                    speed = Vector3.Lerp(speed, Vector3.zero, Time.deltaTime * DesacelerationRate);
                    SetContentAnchoredPosition(speed, Vector2.zero);
                }
            }

            for (int i = 0; i < contentChilds.Count; i++)
            {
                contentChilds[i].UpdateVisibility(rect);
            }

            if (Direction == CircularScrollViewDirection.Horizontal)
            {
                if (speed.x < 0) //move left
                {
                    MoveAllInvisibleTo(false);
                }
                else if (speed.x > 0) //move right
                {
                    MoveAllInvisibleTo(true);
                }
            }

            if (Direction == CircularScrollViewDirection.Vertical)
            {
                if (speed.y < 0)
                {
                    MoveAllInvisibleTo(false);
                }
                else if (speed.y > 0)
                {
                    MoveAllInvisibleTo(true);
                }
            }
        }

        private Vector3 CalcAlignment(float width, float height)
        {
            width = width * 0.5f;
            height = height * 0.5f;

            switch (Alignment)
            {
                case TextAnchor.UpperLeft:
                    alignBuffer.Set(corners[1].x + width, corners[1].y - height, corners[1].z);
                    break;
                case TextAnchor.UpperCenter:
                    alignBuffer.Set((corners[2].x + corners[1].x) / 2.0f, corners[1].y - height, corners[0].z);
                    break;
                case TextAnchor.UpperRight:
                    alignBuffer.Set(corners[2].x - width, corners[2].y - height, corners[2].z);
                    break;

                case TextAnchor.MiddleLeft:
                    alignBuffer.Set(corners[0].x + width, (corners[1].y + corners[0].y) / 2.0f, corners[0].z);
                    break;
                case TextAnchor.MiddleCenter:
                    alignBuffer.Set((corners[2].x + corners[1].x) / 2.0f, (corners[1].y + corners[0].y) / 2.0f,
                        corners[0].z);
                    break;
                case TextAnchor.MiddleRight:
                    alignBuffer.Set(corners[2].x - width, (corners[2].y + corners[3].y) / 2.0f, corners[0].z);
                    break;

                case TextAnchor.LowerLeft:
                    alignBuffer.Set(corners[0].x + width, corners[0].y + height, corners[0].z);
                    break;
                case TextAnchor.LowerCenter:
                    alignBuffer.Set((corners[3].x + corners[0].x) / 2.0f, corners[0].y + height, corners[0].z);
                    break;
                case TextAnchor.LowerRight:
                    alignBuffer.Set(corners[3].x - width, corners[3].y + height, corners[3].z);
                    break;
            }

            return alignBuffer;
        }

        public void Reposition()
        {
            if (FirstSelected != null && contentChilds.Count > 1)
            {
                float attempts = contentChilds.Count;
                while (contentChilds[0].Transform != FirstSelected)
                {
                    contentChilds.MoveForward();
                    attempts--;
                    if (attempts < 0)
                    {
                        break;
                    }
                }
            }

            if (contentChilds.Count > 0)
            {
                contentChilds[0].Transform.position =
                    CalcAlignment(contentChilds[0].Rect.width, contentChilds[0].Rect.height);

                if (Direction == CircularScrollViewDirection.Horizontal)
                {
                    repositionBuffer = contentChilds[0].RectTransform.anchoredPosition.x;
                }

                if (Direction == CircularScrollViewDirection.Vertical)
                {
                    repositionBuffer = contentChilds[0].RectTransform.anchoredPosition.y;
                }
            }

            for (int i = 0; i < contentChilds.Count; i++)
            {
                if (Direction == CircularScrollViewDirection.Horizontal)
                {
                    contentChilds[i].RectTransform.anchoredPosition = new Vector2(repositionBuffer,
                        contentChilds[0].RectTransform.anchoredPosition.y);
                    repositionBuffer += contentChilds[i].RectTransform.rect.width + Spacing;
                }

                if (Direction == CircularScrollViewDirection.Vertical)
                {
                    contentChilds[i].RectTransform.anchoredPosition =
                        new Vector2(contentChilds[0].RectTransform.anchoredPosition.x, repositionBuffer);
                    repositionBuffer += contentChilds[i].RectTransform.rect.height + Spacing;
                }

                contentChilds[i].UpdateVisibility(rect);
                contentChilds[i].UpdateDistance(center, Direction);
            }

            MoveAllInvisibleTo(true);
            MoveAllInvisibleTo(false);
        }

        public GameObject GetSelected()
        {
            if (closestChild != null && SnapAtCenter)
            {
                return closestChild.GameObject;
            }

            return null;
        }

        private void onChange(GameObject newSelected, GameObject oldSelected)
        {
            if (SnapAtCenter)
            {
                OnChangeSelected(newSelected, oldSelected);

                if (OnChange != null)
                {
                    OnChange.Invoke(newSelected, oldSelected);
                }

                OnSelectedChange.Invoke(newSelected);
            }
        }

        protected virtual void OnChangeSelected(GameObject newSelected, GameObject oldSelected)
        {
        }

        private void MoveAllInvisibleTo(bool toStart)
        {
            if (contentChilds.TrueForAll(item => item.IsVisible == false))
            {
                return;
            }

            if (contentChilds.Count > 0)
            {
                ScrollViewItem target = toStart ? contentChilds.Last() : contentChilds.FirstOrDefault();
                ScrollViewItem destination = toStart ? contentChilds.FirstOrDefault() : contentChilds.Last();

                while (!target.IsVisible)
                {
                    if (toStart)
                    {
                        target.Transform.SetAsFirstSibling();
                    }
                    else
                    {
                        target.Transform.SetAsLastSibling();
                    }

                    if (Direction == CircularScrollViewDirection.Horizontal)
                    {
                        target.RectTransform.anchoredPosition = new Vector2(
                            toStart
                                ? destination.RectTransform.anchoredPosition.x - destination.RectTransform.rect.width -
                                  Spacing
                                : destination.RectTransform.anchoredPosition.x + destination.RectTransform.rect.width +
                                  Spacing,
                            destination.RectTransform.anchoredPosition.y);
                    }

                    if (Direction == CircularScrollViewDirection.Vertical)
                    {
                        target.RectTransform.anchoredPosition = new Vector2(
                            destination.RectTransform.anchoredPosition.x,
                            toStart
                                ? destination.RectTransform.anchoredPosition.y - destination.RectTransform.rect.height -
                                  Spacing
                                : destination.RectTransform.anchoredPosition.y + destination.RectTransform.rect.height +
                                  Spacing);
                    }

                    target.UpdateVisibility(rect);
                    target.UpdateDistance(center, Direction);

                    if (toStart)
                    {
                        contentChilds.MoveBackwards();
                    }
                    else
                    {
                        contentChilds.MoveForward();
                    }

                    target = toStart ? contentChilds.Last() : contentChilds.FirstOrDefault();
                    destination = toStart ? contentChilds.FirstOrDefault() : contentChilds.Last();
                }
            }
        }

        private void UpdateCache()
        {
            if (thisRectTransform != null)
            {
                thisRectTransform.GetWorldCorners(corners);
                rect.Set(corners[1].x, corners[1].y, Mathf.Abs(corners[2].x - corners[1].x),
                    Mathf.Abs(corners[0].y - corners[1].y));
                center.Set((corners[2].x + corners[1].x) / 2.0f, (corners[1].y + corners[0].y) / 2.0f, corners[0].z);
            }

            if (thisTransform.childCount != childCount)
            {
                contentChilds.Clear();
                for (int i = 0; i < thisTransform.childCount; i++)
                {
                    contentChilds.Add(new ScrollViewItem(thisTransform.GetChild(i)));

                    if (i == 0)
                    {
                        closestChild = contentChilds[i];
                    }
                }

                Reposition();

                childCount = thisTransform.childCount;
            }
        }

        protected void Reset()
        {
            Awake();
            Start();
        }
    }
}
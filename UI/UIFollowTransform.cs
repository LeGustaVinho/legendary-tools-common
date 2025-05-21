using UnityEngine;

namespace LegendaryTools.UI
{
    [ExecuteInEditMode]
    public class UIFollowTransform : MonoBehaviour
    {
        public enum UpdateModeType
        {
            Update,
            LateUpdate
        }

        public Camera Camera;
        public Canvas Canvas;
        private RectTransform CanvasRectTransform;
        public Vector2 Offset;

        private RectTransform RectTransform;
        public Transform Target;

        private Vector3 targetScreenPointPosition;

        public UpdateModeType UpdateMode;
        private Vector3 worldPointInRectangle;

        private bool IsNotCanvasOverlay => Canvas != null && Canvas.renderMode != RenderMode.ScreenSpaceOverlay;

        private void Awake()
        {
            cache();
        }

        private void Update()
        {
            if (UpdateMode == UpdateModeType.Update)
            {
                Follow();
            }
        }

        private void LateUpdate()
        {
            if (UpdateMode == UpdateModeType.LateUpdate)
            {
                Follow();
            }
        }

        private void Reset()
        {
            cache();
        }

        private void cache()
        {
            RectTransform = GetComponent<RectTransform>();
            if (Canvas != null)
            {
                CanvasRectTransform = Canvas.GetComponent<RectTransform>();
            }
        }

        public void Follow()
        {
            if (Target == null || Canvas == null)
            {
                return;
            }

            if (Canvas.renderMode == RenderMode.ScreenSpaceCamera && Camera == null)
            {
                return;
            }

            if (RectTransform == null || CanvasRectTransform == null)
            {
                cache();
            }

            if (Camera != null)
            {
                targetScreenPointPosition = Camera.WorldToScreenPoint(Target.position);
            }

            RectTransformUtility.ScreenPointToWorldPointInRectangle(CanvasRectTransform, targetScreenPointPosition,
                Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera, out worldPointInRectangle);
            RectTransform.position = worldPointInRectangle;
            RectTransform.anchoredPosition += Offset;
        }
    }
}
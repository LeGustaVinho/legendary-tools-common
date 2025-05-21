using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.UI
{
    /// <summary>
    /// Connects two UI RectTransforms with a dynamic line inside a Canvas.
    /// Supports pooling, optional destruction, material override, and render order control.
    /// </summary>
    [ExecuteAlways]
    public class UILineConnector : MonoBehaviour
    {
        public RectTransform pointA;
        public RectTransform pointB;
        public Image lineImage;
        public float thickness = 2f;

        private Canvas canvas;

        private static readonly Stack<UILineConnector> pool = new Stack<UILineConnector>();

        void LateUpdate()
        {
            if (pointA == null || pointB == null || lineImage == null)
                return;

            if (canvas == null)
            {
                canvas = lineImage.canvas;
                if (canvas == null)
                {
                    Debug.LogError("Line image must be inside a Canvas.");
                    return;
                }
            }

            // Get world positions of the points
            Vector3 worldA = pointA.position;
            Vector3 worldB = pointB.position;

            // Convert to local space of the line's parent
            RectTransform parentRect = lineImage.rectTransform.parent as RectTransform;
            Vector2 localA = parentRect.InverseTransformPoint(worldA);
            Vector2 localB = parentRect.InverseTransformPoint(worldB);

            // Calculate direction, distance, and angle
            Vector2 direction = localB - localA;
            float distance = direction.magnitude;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            // Configure the line's RectTransform
            RectTransform lineRect = lineImage.rectTransform;
            lineRect.pivot = new Vector2(0f, 0.5f); // always pivot from the start of the line
            lineRect.sizeDelta = new Vector2(distance, thickness);
            lineRect.localPosition = localA;
            lineRect.localRotation = Quaternion.Euler(0, 0, angle);
        }

        /// <summary>
        /// Schedules this line to be destroyed after the given time (only in play mode).
        /// </summary>
        /// <param name="seconds">Time in seconds before destruction.</param>
        public void DestroyAfter(float seconds)
        {
            if (Application.isPlaying)
                Destroy(gameObject, seconds);
        }

        /// <summary>
        /// Recycles this line back into the internal pool.
        /// </summary>
        public void Recycle()
        {
            gameObject.SetActive(false);
            pointA = null;
            pointB = null;
            lineImage = null;
            canvas = null;
            pool.Push(this);
        }

        /// <summary>
        /// Clears the static pool of reusable line objects.
        /// </summary>
        public static void ClearPool()
        {
            pool.Clear();
        }

        /// <summary>
        /// Creates and returns a new UILineConnector instance connecting two UI points.
        /// </summary>
        /// <param name="pointA">The starting point of the line.</param>
        /// <param name="pointB">The ending point of the line.</param>
        /// <param name="canvas">Canvas in which the line will be rendered.</param>
        /// <param name="parent">Optional parent for the line GameObject.</param>
        /// <param name="lineSprite">Optional sprite to use for the line Image.</param>
        /// <param name="material">Optional custom material for the line.</param>
        /// <param name="color">Optional color for the line.</param>
        /// <param name="thickness">Thickness in pixels.</param>
        /// <param name="name">GameObject name.</param>
        /// <param name="destroyAfterSeconds">If > 0, destroys the line after X seconds.</param>
        /// <param name="siblingIndex">Set sibling index (render order in parent). -1 means ignore.</param>
        /// <returns>The UILineConnector instance.</returns>
        public static UILineConnector Create(
            RectTransform pointA,
            RectTransform pointB,
            Canvas canvas,
            Transform parent = null,
            Sprite lineSprite = null,
            Material material = null,
            Color? color = null,
            float thickness = 2f,
            string name = "UILineConnector",
            float destroyAfterSeconds = -1f,
            int siblingIndex = -1
        )
        {
            if (canvas == null)
            {
                Debug.LogError("Canvas cannot be null.");
                return null;
            }

            UILineConnector connector;
            GameObject obj;

            if (pool.Count > 0)
            {
                connector = pool.Pop();
                obj = connector.gameObject;
                obj.SetActive(true);
            }
            else
            {
                obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UILineConnector));
                connector = obj.GetComponent<UILineConnector>();
            }

            if (parent == null)
                parent = canvas.transform;

            obj.transform.SetParent(parent, false);

            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0, 0.5f);

            Image img = obj.GetComponent<Image>();
            img.sprite = lineSprite;
            img.material = material;
            img.color = color ?? Color.white;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;

            connector.canvas = canvas;
            connector.pointA = pointA;
            connector.pointB = pointB;
            connector.lineImage = img;
            connector.thickness = thickness;

            if (destroyAfterSeconds > 0f)
                connector.DestroyAfter(destroyAfterSeconds);

            if (siblingIndex >= 0)
                obj.transform.SetSiblingIndex(siblingIndex);

            return connector;
        }
    }
}
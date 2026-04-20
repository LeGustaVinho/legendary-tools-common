using UnityEngine;

namespace LegendaryTools.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class UISafeArea : MonoBehaviour
    {
        private RectTransform rectTransform;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        private Rect lastSafeArea = Rect.zero;
        private Vector2Int lastScreenSize = Vector2Int.zero;
        private ScreenOrientation lastOrientation = ScreenOrientation.AutoRotation;

        // Offsets adicionais em pixels
        [SerializeField, Tooltip("Offset superior em pixels.")]
        private float topOffset = 0f;

        [SerializeField, Tooltip("Offset inferior em pixels.")]
        private float bottomOffset = 0f;

        [SerializeField, Tooltip("Offset esquerdo em pixels.")]
        private float leftOffset = 0f;

        [SerializeField, Tooltip("Offset direito em pixels.")]
        private float rightOffset = 0f;

        void Start()
        {
            rectTransform = GetComponent<RectTransform>();
            ApplySafeArea();
            // Inicializa os rastreadores
            lastSafeArea = Screen.safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            lastOrientation = Screen.orientation;
        }

        void Update()
        {
            // Verifica se a área segura mudou
            bool safeAreaChanged = Screen.safeArea != lastSafeArea;
            // Verifica se o tamanho da tela mudou
            bool screenSizeChanged = Screen.width != lastScreenSize.x || Screen.height != lastScreenSize.y;
            // Verifica se a orientação mudou
            bool orientationChanged = Screen.orientation != lastOrientation;

            if (safeAreaChanged || screenSizeChanged || orientationChanged)
            {
                ApplySafeArea();
                // Atualiza os rastreadores
                lastSafeArea = Screen.safeArea;
                lastScreenSize = new Vector2Int(Screen.width, Screen.height);
                lastOrientation = Screen.orientation;
            }
        }

        private void ApplySafeArea()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return;

            Rect safeArea = Screen.safeArea;

            // Aplica os offsets
            safeArea.xMin += leftOffset;
            safeArea.xMax -= rightOffset;
            safeArea.yMin += bottomOffset;
            safeArea.yMax -= topOffset;

            // Assegura que a área segura não fique com valores inválidos
            safeArea.xMin = Mathf.Clamp(safeArea.xMin, 0, Screen.width);
            safeArea.xMax = Mathf.Clamp(safeArea.xMax, safeArea.xMin, Screen.width);
            safeArea.yMin = Mathf.Clamp(safeArea.yMin, 0, Screen.height);
            safeArea.yMax = Mathf.Clamp(safeArea.yMax, safeArea.yMin, Screen.height);

            RectTransform parentRectTransform = rectTransform.parent as RectTransform;
            if (parentRectTransform == null)
                return;

            Rect parentRect = parentRectTransform.rect;
            Vector2 parentSize = parentRect.size;

            Vector2 safeAreaMin = safeArea.position;
            Vector2 safeAreaMax = safeArea.position + safeArea.size;

            safeAreaMin.x /= Screen.width;
            safeAreaMin.y /= Screen.height;
            safeAreaMax.x /= Screen.width;
            safeAreaMax.y /= Screen.height;

            Vector2 targetMin = parentRect.min + Vector2.Scale(safeAreaMin, parentSize);
            Vector2 targetMax = parentRect.min + Vector2.Scale(safeAreaMax, parentSize);
            Vector2 targetSize = targetMax - targetMin;
            Vector2 targetPivotPosition = targetMin + Vector2.Scale(targetSize, rectTransform.pivot);

            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetSize.x);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetSize.y);

            Vector2 anchorPosition = rectTransform.anchorMin +
                                     Vector2.Scale(rectTransform.anchorMax - rectTransform.anchorMin, rectTransform.pivot);
            Vector2 anchorReference = parentRect.min + Vector2.Scale(anchorPosition, parentSize);
            rectTransform.anchoredPosition = targetPivotPosition - anchorReference;
        }

#if UNITY_EDITOR
        // Atualiza a área segura no editor quando os valores de offset mudam
        void OnValidate()
        {
            if (rectTransform == null)
                rectTransform = GetComponent<RectTransform>();

            ApplySafeArea();
            // Atualiza os rastreadores
            lastSafeArea = Screen.safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            lastOrientation = Screen.orientation;
        }
#endif
    }
}

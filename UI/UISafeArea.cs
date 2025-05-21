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

            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;

            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
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
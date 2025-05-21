using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LegendaryTools.UI.Editor
{
    public class SceneUiObjectsTagger
    {
        private static readonly List<System.Type> ComponentPriorityList = new List<System.Type>()
        {
            typeof(Canvas),
            typeof(CanvasGroup),
            typeof(VerticalLayoutGroup),
            typeof(HorizontalLayoutGroup),
            typeof(GridLayoutGroup),
            typeof(Button),
            typeof(Toggle),
            typeof(Slider),
            typeof(ScrollRect),
            typeof(Scrollbar),
            typeof(TMP_InputField),
            typeof(InputField),
            typeof(TMP_Dropdown),
            typeof(Dropdown),
            typeof(Mask),
            typeof(RectMask2D),
            typeof(TextMeshProUGUI),
            typeof(Text),
            typeof(Image),
            typeof(RawImage),
            typeof(ToggleGroup),
        };

        private static Dictionary<Type, string> TypeNames = new Dictionary<Type, string>()
        {
            {typeof(VerticalLayoutGroup), "Vertical"},
            {typeof(HorizontalLayoutGroup), "Horizontal"},
            {typeof(GridLayoutGroup), "Grid"},
            {typeof(TMP_InputField), "InputField"},
            {typeof(TMP_Dropdown), "Dropdown"},
            {typeof(RectMask2D), "Mask"},
            {typeof(TextMeshProUGUI), "Text"},
        };

        [MenuItem("Tools/LegendaryTools/Tag All UI Objects in Scene")]
        public static void TagGameObjects()
        {
            foreach (GameObject obj in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (obj.name.Contains('[') && obj.name.Contains(']')) continue;
                Canvas canvas = obj.GetComponentInParent<Canvas>();
                bool consumed = false;
                foreach (Type type in ComponentPriorityList)
                {
                    Component component = obj.GetComponent(type);
                    if (component == null || canvas == null) continue;
                    obj.name = TypeNames.TryGetValue(component.GetType(), out string typeName)
                        ? $"[{typeName}] {obj.name}"
                        : $"[{type.Name}] {obj.name}";
                    consumed = true;
                    break;
                }

                if (!consumed && canvas != null) obj.name = $"[Group] {obj.name}";
            }
        }
    }
}
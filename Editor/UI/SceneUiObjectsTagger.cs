using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LegendaryTools.UI.Editor
{
    public class SceneUiObjectsTagger
    {
        private static readonly List<Type> ComponentPriorityList = new()
        {
            typeof(EventSystem),
            typeof(StandaloneInputModule),

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

            typeof(ContentSizeFitter),
            typeof(AspectRatioFitter),
            typeof(LayoutElement),

            // UI effects (still affect rendering)
            typeof(Shadow),
            typeof(Outline),
            typeof(PositionAsUV1)
        };

        private static readonly Dictionary<Type, string> TypeNames = new()
        {
            { typeof(VerticalLayoutGroup), "Vertical" },
            { typeof(HorizontalLayoutGroup), "Horizontal" },
            { typeof(GridLayoutGroup), "Grid" },
            { typeof(TMP_InputField), "InputField" },
            { typeof(TMP_Dropdown), "Dropdown" },
            { typeof(RectMask2D), "Mask" },
            { typeof(TextMeshProUGUI), "Text" }
        };

        // Matches: "[Anything] " at the very beginning of the name.
        private static readonly Regex TagPrefixRegex = new(@"^\[[^\]]+\]\s", RegexOptions.Compiled);

        [MenuItem("Tools/LegendaryTools/UI/Tag All UI Objects in Scene")]
        public static void TagGameObjects()
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Tag UI Objects");

            bool anyChanged = false;
            HashSet<Scene> dirtyScenes = new();

            GameObject prefabRootIfAny = null;
            bool isPrefabMode = TryGetPrefabStageRoot(out prefabRootIfAny);

            foreach (GameObject obj in EnumerateTargetGameObjects(prefabRootIfAny))
            {
                if (obj == null) continue;

                // Performance: skip non-UI objects early.
                if (obj.GetComponent<RectTransform>() == null) continue;

                // Ignore common "do not touch" prefixes.
                if (StartsWithIgnoredPrefix(obj.name)) continue;

                // Skip objects that already have a leading tag prefix like "[Button] ".
                if (IsAlreadyTagged(obj.name)) continue;

                // Only tag objects that are inside a Canvas hierarchy.
                Canvas canvas = obj.GetComponentInParent<Canvas>();
                if (canvas == null) continue;

                string newName = BuildTaggedName(obj);
                if (newName == obj.name) continue;

                Undo.RecordObject(obj, "Tag UI Objects");
                obj.name = newName;

                anyChanged = true;

                // Mark the owning scene dirty so changes are recognized by Unity.
                dirtyScenes.Add(obj.scene);
            }

            if (anyChanged)
            {
                if (isPrefabMode && prefabRootIfAny != null)
                {
                    // Ensure prefab stage contents are recognized as modified.
                    EditorUtility.SetDirty(prefabRootIfAny);

                    // Also mark the prefab stage scene dirty when possible.
                    dirtyScenes.Add(prefabRootIfAny.scene);
                }

                foreach (Scene scene in dirtyScenes)
                {
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        private static string BuildTaggedName(GameObject obj)
        {
            foreach (Type type in ComponentPriorityList)
            {
                Component component = obj.GetComponent(type);
                if (component == null) continue;

                if (TypeNames.TryGetValue(component.GetType(), out string typeName))
                    return $"[{typeName}] {obj.name}";

                return $"[{type.Name}] {obj.name}";
            }

            // No prioritized component found but it's under a Canvas: treat as a grouping object.
            return $"[Group] {obj.name}";
        }

        private static bool IsAlreadyTagged(string objectName)
        {
            return TagPrefixRegex.IsMatch(objectName);
        }

        private static bool StartsWithIgnoredPrefix(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return false;
            char c = objectName[0];
            return c == '!' || c == '_';
        }

        private static IEnumerable<GameObject> EnumerateTargetGameObjects(GameObject prefabRootIfAny)
        {
            // If Prefab Mode is active, tag only the prefab contents root and its children.
            if (prefabRootIfAny != null)
            {
                Transform[] transforms = prefabRootIfAny.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    yield return transforms[i].gameObject;
                }

                yield break;
            }

            // Otherwise, tag objects from open scenes.
            foreach (GameObject obj in Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                yield return obj;
            }
        }

        private static bool TryGetPrefabStageRoot(out GameObject prefabContentsRoot)
        {
            prefabContentsRoot = null;

#if UNITY_2018_3_OR_NEWER
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.prefabContentsRoot != null)
            {
                prefabContentsRoot = prefabStage.prefabContentsRoot;
                return true;
            }
#endif
            return false;
        }
    }
}
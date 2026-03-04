using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendaryTools.Editor
{
    public sealed class ReferenceTransferTemplate : ScriptableObject
    {
        public List<ReferenceTransferMappingEntry> referenceEntries = new();
        public List<ComponentCopyMappingEntry> componentCopyEntries = new();
    }

    [Serializable]
    public sealed class ReferenceTransferMappingEntry
    {
        public ReferenceTransferObjectDescriptor source;
        public ReferenceTransferObjectDescriptor target;
    }

    [Serializable]
    public sealed class ComponentCopyMappingEntry
    {
        public ReferenceTransferObjectDescriptor source;
        public ReferenceTransferObjectDescriptor target;
    }

    [Serializable]
    public sealed class ReferenceTransferObjectDescriptor
    {
        public ReferenceTransferObjectKind kind;
        public string relativePath;
        public string stablePath;
        public string componentTypeName;
        public int componentIndex;

        public string ToStableKey()
        {
            if (string.IsNullOrEmpty(stablePath)) return ToLegacyKey();

            return $"{kind}|{stablePath}|{componentTypeName}|{componentIndex}";
        }

        public string ToLegacyKey()
        {
            return $"{kind}|{relativePath}|{componentTypeName}|{componentIndex}";
        }

        public string ToDisplayString()
        {
            if (kind == ReferenceTransferObjectKind.GameObject)
                return string.IsNullOrEmpty(relativePath) ? "<Root>" : relativePath;

            string typeName = string.IsNullOrEmpty(componentTypeName)
                ? "Component"
                : GetShortTypeName(componentTypeName);

            string path = string.IsNullOrEmpty(relativePath) ? "<Root>" : relativePath;
            return $"{path} [{typeName} #{componentIndex}]";
        }

        private static string GetShortTypeName(string assemblyQualifiedName)
        {
            Type type = ReferenceTransferTypeUtility.FindType(assemblyQualifiedName);
            return type != null ? type.Name : assemblyQualifiedName;
        }
    }

    public enum ReferenceTransferObjectKind
    {
        GameObject = 0,
        Component = 1
    }

    public sealed class ReferenceTransferToolWindow : EditorWindow
    {
        private const string TemplateAssetDefaultName = "ReferenceTransferTemplate";
        private const float ColumnSpacing = 8f;
        private const float MinColumnWidth = 260f;
        private const float ColumnHeight = 420f;

        [SerializeField] private GameObject sourceRoot;
        [SerializeField] private GameObject targetRoot;
        [SerializeField] private ReferenceTransferTemplate templateAsset;

        [SerializeField] private Vector2 leftScrollPosition;
        [SerializeField] private Vector2 rightScrollPosition;

        [SerializeField] private List<ReferenceUsageMappingItem> referenceMappingItems = new();

        [SerializeField] private List<ComponentCopyMappingItem> componentCopyMappingItems = new();

        [SerializeField] private int addCopySourceSelectionIndex;

        private readonly Dictionary<string, UnityEngine.Object> currentSourceObjectsByKey = new();

        private readonly Dictionary<string, UnityEngine.Object> currentTargetObjectsByKey = new();

        private readonly List<Component> availableSourceComponentsForSelection = new();

        [MenuItem("Tools/LegendaryTools/Automation/Reference Transfer Tool")]
        public static void Open()
        {
            GetWindow<ReferenceTransferToolWindow>("Reference Transfer");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Source / Target", EditorStyles.boldLabel);

                sourceRoot =
                    (GameObject)EditorGUILayout.ObjectField("GameObject A", sourceRoot, typeof(GameObject), true);
                targetRoot =
                    (GameObject)EditorGUILayout.ObjectField("GameObject B", targetRoot, typeof(GameObject), true);
                templateAsset = (ReferenceTransferTemplate)EditorGUILayout.ObjectField("Template", templateAsset,
                    typeof(ReferenceTransferTemplate), false);

                EditorGUILayout.Space();

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = sourceRoot != null && targetRoot != null;
                    if (GUILayout.Button("Scan References", GUILayout.Height(28))) RebuildScan();

                    GUI.enabled = templateAsset != null && HasAnyMappings();
                    if (GUILayout.Button("Load Template", GUILayout.Height(28))) ApplyTemplateToCurrentMappings();

                    GUI.enabled = HasAnyMappings();
                    if (GUILayout.Button("Create Template Asset", GUILayout.Height(28)))
                        CreateTemplateAssetFromCurrentMappings();

                    GUI.enabled = templateAsset != null && HasAnyMappings();
                    if (GUILayout.Button("Save Into Template", GUILayout.Height(28))) SaveCurrentMappingsIntoTemplate();

                    GUI.enabled = true;
                }
            }

            DrawScopeInfo();
            EditorGUILayout.Space();

            if (sourceRoot == null || targetRoot == null)
            {
                EditorGUILayout.HelpBox(
                    "Select GameObject A and GameObject B, then click Scan References.",
                    MessageType.Info);
                return;
            }

            DrawSummary();
            EditorGUILayout.Space();

            DrawTwoColumnLayoutFixed();

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = HasAnyMappings();
                if (GUILayout.Button("Transfer", GUILayout.Height(32))) ExecuteTransfer();

                GUI.enabled = true;
            }
        }

        private void DrawScopeInfo()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            string scopeText = prefabStage != null
                ? $"Scan Scope: Current Prefab Mode ({prefabStage.prefabContentsRoot.name})"
                : "Scan Scope: Current Open Scene(s)";

            EditorGUILayout.HelpBox(scopeText, MessageType.None);
        }

        private void DrawSummary()
        {
            int totalReferenceUsages = referenceMappingItems.Sum(x => x.usages.Count);
            int mappedReferenceCount = referenceMappingItems.Count(x => x.mappedTarget != null);
            int mappedComponentCopyCount = componentCopyMappingItems.Count(x => x.mappedTarget != null);

            EditorGUILayout.LabelField(
                $"Reference usages found: {totalReferenceUsages} | Reference mappings: {mappedReferenceCount} | Copy mappings: {mappedComponentCopyCount}",
                EditorStyles.boldLabel);
        }

        private void DrawTwoColumnLayoutFixed()
        {
            float totalWidth = Mathf.Max(0f, EditorGUIUtility.currentViewWidth - 40f);
            float columnWidth = Mathf.Max(MinColumnWidth, (totalWidth - ColumnSpacing) * 0.5f);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(columnWidth),
                           GUILayout.Height(ColumnHeight)))
                {
                    leftScrollPosition =
                        EditorGUILayout.BeginScrollView(leftScrollPosition, GUILayout.ExpandHeight(true));
                    DrawReferenceTransferSection();
                    EditorGUILayout.EndScrollView();
                }

                GUILayout.Space(ColumnSpacing);

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(columnWidth),
                           GUILayout.Height(ColumnHeight)))
                {
                    rightScrollPosition =
                        EditorGUILayout.BeginScrollView(rightScrollPosition, GUILayout.ExpandHeight(true));
                    DrawComponentCopySection();
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private bool HasAnyMappings()
        {
            return referenceMappingItems.Count > 0 || componentCopyMappingItems.Count > 0;
        }

        private void DrawReferenceTransferSection()
        {
            EditorGUILayout.LabelField("Reference Transfer (A -> B)", EditorStyles.boldLabel);

            if (referenceMappingItems.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No serialized references to A or its sub-objects/components were found in the current scan scope.",
                    MessageType.Info);
                return;
            }

            foreach (ReferenceUsageMappingItem item in referenceMappingItems)
            {
                DrawReferenceMappingItem(item);
            }
        }

        private void DrawComponentCopySection()
        {
            EditorGUILayout.LabelField("Copy Value (A -> B)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Only components explicitly selected below are listed here for mapping and copy.",
                MessageType.None);

            DrawAddCopySourceControls();

            if (componentCopyMappingItems.Count == 0)
            {
                EditorGUILayout.HelpBox("No source components selected for copy.", MessageType.Info);
                return;
            }

            for (int i = 0; i < componentCopyMappingItems.Count; i++)
            {
                DrawComponentCopyItem(componentCopyMappingItems[i], i);
            }
        }

        private void DrawAddCopySourceControls()
        {
            RefreshAvailableSourceComponentsForSelection();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Add source component", EditorStyles.miniBoldLabel);

                if (availableSourceComponentsForSelection.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "All eligible components from A are already added, or A has no eligible components.",
                        MessageType.Info);
                    return;
                }

                string[] options = BuildAvailableSourceComponentOptions();
                addCopySourceSelectionIndex = Mathf.Clamp(addCopySourceSelectionIndex, 0, options.Length - 1);

                using (new EditorGUILayout.HorizontalScope())
                {
                    addCopySourceSelectionIndex = EditorGUILayout.Popup(addCopySourceSelectionIndex, options);

                    if (GUILayout.Button("Add", GUILayout.Width(70f))) AddSelectedCopySource();
                }
            }
        }

        private void DrawReferenceMappingItem(ReferenceUsageMappingItem item)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(item.sourceDescriptor.ToDisplayString(), EditorStyles.boldLabel);
                EditorGUILayout.ObjectField("Source Object", item.sourceObject, typeof(UnityEngine.Object), true);

                List<UnityEngine.Object> compatibleTargets = GetCompatibleTargetsForReference(item);
                string[] options = BuildReferenceTargetOptions(compatibleTargets);

                int currentIndex = 0;
                if (item.mappedTarget != null)
                {
                    int foundIndex = compatibleTargets.IndexOf(item.mappedTarget);
                    currentIndex = foundIndex >= 0 ? foundIndex + 1 : 0;
                }

                int newIndex = EditorGUILayout.Popup("Map To", currentIndex, options);
                item.mappedTarget = newIndex <= 0 ? null : compatibleTargets[newIndex - 1];

                if (item.mappedTarget != null)
                    EditorGUILayout.ObjectField("Mapped Target", item.mappedTarget, typeof(UnityEngine.Object), true);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Usages ({item.usages.Count})", EditorStyles.miniBoldLabel);

                foreach (ReferenceUsage usage in item.usages)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.ObjectField("Owner", usage.ownerComponent, typeof(Component), true);
                        EditorGUILayout.LabelField("Property", usage.propertyPath);
                        EditorGUILayout.LabelField("Location", usage.ownerHierarchyPath);
                    }
                }
            }
        }

        private void DrawComponentCopyItem(ComponentCopyMappingItem item, int index)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(item.sourceDescriptor.ToDisplayString(), EditorStyles.boldLabel);

                    if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                    {
                        componentCopyMappingItems.RemoveAt(index);
                        RefreshAvailableSourceComponentsForSelection();
                        return;
                    }
                }

                EditorGUILayout.ObjectField("Source Component", item.sourceComponent, typeof(Component), true);

                List<Component> compatibleTargets = GetCompatibleTargetComponentsForCopy(item.sourceComponent);
                string[] options = BuildComponentTargetOptions(compatibleTargets);

                int currentIndex = 0;
                if (item.mappedTarget != null)
                {
                    int foundIndex = compatibleTargets.IndexOf(item.mappedTarget);
                    currentIndex = foundIndex >= 0 ? foundIndex + 1 : 0;
                }

                int newIndex = EditorGUILayout.Popup("Copy To", currentIndex, options);
                item.mappedTarget = newIndex <= 0 ? null : compatibleTargets[newIndex - 1];

                if (item.mappedTarget != null)
                    EditorGUILayout.ObjectField("Target Component", item.mappedTarget, typeof(Component), true);
            }
        }

        private void AddSelectedCopySource()
        {
            if (availableSourceComponentsForSelection.Count == 0) return;

            int safeIndex = Mathf.Clamp(addCopySourceSelectionIndex, 0,
                availableSourceComponentsForSelection.Count - 1);
            Component sourceComponent = availableSourceComponentsForSelection[safeIndex];
            if (sourceComponent == null) return;

            if (!TryBuildDescriptorForSource(sourceComponent,
                    out ReferenceTransferObjectDescriptor sourceDescriptor)) return;

            ComponentCopyMappingItem newItem = new()
            {
                sourceComponent = sourceComponent,
                sourceDescriptor = sourceDescriptor,
                mappedTarget = null
            };

            componentCopyMappingItems.Add(newItem);

            componentCopyMappingItems = componentCopyMappingItems
                .OrderBy(x => x.sourceDescriptor.relativePath, StringComparer.Ordinal)
                .ThenBy(x => x.sourceComponent.GetType().Name, StringComparer.Ordinal)
                .ThenBy(x => x.sourceDescriptor.componentIndex)
                .ToList();

            RefreshAvailableSourceComponentsForSelection();
            addCopySourceSelectionIndex = 0;

            if (templateAsset != null) ApplyTemplateToComponentCopyItem(newItem);
        }

        private void RefreshAvailableSourceComponentsForSelection()
        {
            availableSourceComponentsForSelection.Clear();

            if (sourceRoot == null) return;

            HashSet<Component> alreadyAdded = new(
                componentCopyMappingItems
                    .Where(x => x.sourceComponent != null)
                    .Select(x => x.sourceComponent));

            foreach (Transform transform in sourceRoot.GetComponentsInChildren<Transform>(true))
            {
                Component[] components = transform.GetComponents<Component>();
                foreach (Component component in components)
                {
                    if (component == null) continue;

                    if (!IsEligibleSourceComponentForCopy(component)) continue;

                    if (alreadyAdded.Contains(component)) continue;

                    availableSourceComponentsForSelection.Add(component);
                }
            }

            availableSourceComponentsForSelection.Sort((a, b) =>
            {
                string aLabel = BuildSourceComponentLabel(a);
                string bLabel = BuildSourceComponentLabel(b);
                return string.CompareOrdinal(aLabel, bLabel);
            });

            if (availableSourceComponentsForSelection.Count == 0)
                addCopySourceSelectionIndex = 0;
            else
            {
                addCopySourceSelectionIndex = Mathf.Clamp(addCopySourceSelectionIndex, 0,
                    availableSourceComponentsForSelection.Count - 1);
            }
        }

        private string[] BuildAvailableSourceComponentOptions()
        {
            string[] options = new string[availableSourceComponentsForSelection.Count];

            for (int i = 0; i < availableSourceComponentsForSelection.Count; i++)
            {
                options[i] = BuildSourceComponentLabel(availableSourceComponentsForSelection[i]);
            }

            return options;
        }

        private string BuildSourceComponentLabel(Component component)
        {
            string path = GetRelativePath(sourceRoot.transform, component.transform);
            if (string.IsNullOrEmpty(path)) path = "<Root>";

            int index = GetComponentIndex(component);
            return $"{path} [{component.GetType().Name} #{index}]";
        }

        private void RebuildScan()
        {
            referenceMappingItems.Clear();
            currentSourceObjectsByKey.Clear();
            currentTargetObjectsByKey.Clear();

            if (sourceRoot == null || targetRoot == null)
            {
                componentCopyMappingItems.Clear();
                availableSourceComponentsForSelection.Clear();
                return;
            }

            Dictionary<UnityEngine.Object, ReferenceTransferObjectDescriptor> sourceObjects =
                CollectObjectsUnderRoot(sourceRoot, currentSourceObjectsByKey);

            CollectObjectsUnderRoot(targetRoot, currentTargetObjectsByKey);

            BuildReferenceUsageMappings(sourceObjects);
            SanitizeComponentCopyMappings();
            RefreshAvailableSourceComponentsForSelection();

            if (templateAsset != null) ApplyTemplateToCurrentMappings();
        }

        private void BuildReferenceUsageMappings(
            Dictionary<UnityEngine.Object, ReferenceTransferObjectDescriptor> sourceObjects)
        {
            Dictionary<UnityEngine.Object, ReferenceUsageMappingItem> groupedItems = new();

            foreach (Component component in EnumerateScopeComponents())
            {
                if (component == null) continue;

                ScanComponentForReferenceUsages(component, sourceObjects, groupedItems);
            }

            referenceMappingItems = groupedItems.Values
                .OrderBy(x => x.sourceDescriptor.relativePath, StringComparer.Ordinal)
                .ThenBy(x => x.sourceDescriptor.kind)
                .ThenBy(x => x.sourceDescriptor.componentTypeName, StringComparer.Ordinal)
                .ThenBy(x => x.sourceDescriptor.componentIndex)
                .ToList();
        }

        private void SanitizeComponentCopyMappings()
        {
            List<ComponentCopyMappingItem> validItems = new();

            foreach (ComponentCopyMappingItem item in componentCopyMappingItems)
            {
                if (item == null || item.sourceComponent == null) continue;

                if (!IsUnderSourceRoot(item.sourceComponent)) continue;

                if (!TryBuildDescriptorForSource(item.sourceComponent,
                        out ReferenceTransferObjectDescriptor sourceDescriptor))
                    continue;

                if (!IsEligibleSourceComponentForCopy(item.sourceComponent)) continue;

                item.sourceDescriptor = sourceDescriptor;

                if (item.mappedTarget != null &&
                    (!IsUnderTargetRoot(item.mappedTarget) || !IsEligibleTargetComponentForCopy(item.mappedTarget)))
                    item.mappedTarget = null;

                validItems.Add(item);
            }

            componentCopyMappingItems = validItems
                .OrderBy(x => x.sourceDescriptor.relativePath, StringComparer.Ordinal)
                .ThenBy(x => x.sourceComponent.GetType().Name, StringComparer.Ordinal)
                .ThenBy(x => x.sourceDescriptor.componentIndex)
                .ToList();
        }

        private bool IsUnderSourceRoot(Component component)
        {
            if (sourceRoot == null || component == null) return false;

            return component.transform == sourceRoot.transform || component.transform.IsChildOf(sourceRoot.transform);
        }

        private bool IsUnderTargetRoot(Component component)
        {
            if (targetRoot == null || component == null) return false;

            return component.transform == targetRoot.transform || component.transform.IsChildOf(targetRoot.transform);
        }

        private bool TryBuildDescriptorForSource(Component sourceComponent,
            out ReferenceTransferObjectDescriptor descriptor)
        {
            descriptor = null;

            if (sourceRoot == null || sourceComponent == null) return false;

            if (!IsUnderSourceRoot(sourceComponent)) return false;

            descriptor = new ReferenceTransferObjectDescriptor
            {
                kind = ReferenceTransferObjectKind.Component,
                relativePath = GetRelativePath(sourceRoot.transform, sourceComponent.transform),
                stablePath = GetStableRelativePath(sourceRoot.transform, sourceComponent.transform),
                componentTypeName = sourceComponent.GetType().AssemblyQualifiedName,
                componentIndex = GetComponentIndex(sourceComponent)
            };

            return true;
        }

        private void ScanComponentForReferenceUsages(
            Component component,
            Dictionary<UnityEngine.Object, ReferenceTransferObjectDescriptor> sourceObjects,
            Dictionary<UnityEngine.Object, ReferenceUsageMappingItem> groupedItems)
        {
            SerializedObject serializedObject;

            try
            {
                serializedObject = new SerializedObject(component);
            }
            catch
            {
                return;
            }

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = true;

                if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;

                UnityEngine.Object referencedObject = iterator.objectReferenceValue;
                if (referencedObject == null) continue;

                if (!sourceObjects.TryGetValue(referencedObject, out ReferenceTransferObjectDescriptor descriptor))
                    continue;

                if (!groupedItems.TryGetValue(referencedObject, out ReferenceUsageMappingItem item))
                {
                    item = new ReferenceUsageMappingItem
                    {
                        sourceObject = referencedObject,
                        sourceDescriptor = descriptor,
                        mappedTarget = null,
                        usages = new List<ReferenceUsage>()
                    };

                    groupedItems.Add(referencedObject, item);
                }

                item.usages.Add(new ReferenceUsage
                {
                    ownerComponent = component,
                    propertyPath = iterator.propertyPath,
                    ownerHierarchyPath = GetHierarchyPath(component.gameObject)
                });
            }
        }

        private IEnumerable<Component> EnumerateScopeComponents()
        {
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.prefabContentsRoot != null)
                return prefabStage.prefabContentsRoot.GetComponentsInChildren<Component>(true);

            List<Component> components = new();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    components.AddRange(root.GetComponentsInChildren<Component>(true));
                }
            }

            return components;
        }

        private Dictionary<UnityEngine.Object, ReferenceTransferObjectDescriptor> CollectObjectsUnderRoot(
            GameObject root,
            Dictionary<string, UnityEngine.Object> byKey)
        {
            Dictionary<UnityEngine.Object, ReferenceTransferObjectDescriptor> result = new();

            byKey.Clear();

            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                string relativePath = GetRelativePath(root.transform, transform);
                string stablePath = GetStableRelativePath(root.transform, transform);

                GameObject gameObject = transform.gameObject;
                ReferenceTransferObjectDescriptor gameObjectDescriptor = new()
                {
                    kind = ReferenceTransferObjectKind.GameObject,
                    relativePath = relativePath,
                    stablePath = stablePath,
                    componentTypeName = string.Empty,
                    componentIndex = 0
                };

                result[gameObject] = gameObjectDescriptor;
                byKey[gameObjectDescriptor.ToStableKey()] = gameObject;
                byKey[gameObjectDescriptor.ToLegacyKey()] = gameObject;

                Component[] components = transform.GetComponents<Component>();
                Dictionary<Type, int> indicesByType = new();

                foreach (Component component in components)
                {
                    if (component == null) continue;

                    Type type = component.GetType();
                    indicesByType.TryGetValue(type, out int currentIndex);

                    ReferenceTransferObjectDescriptor componentDescriptor = new()
                    {
                        kind = ReferenceTransferObjectKind.Component,
                        relativePath = relativePath,
                        stablePath = stablePath,
                        componentTypeName = type.AssemblyQualifiedName,
                        componentIndex = currentIndex
                    };

                    result[component] = componentDescriptor;
                    byKey[componentDescriptor.ToStableKey()] = component;
                    byKey[componentDescriptor.ToLegacyKey()] = component;

                    indicesByType[type] = currentIndex + 1;
                }
            }

            return result;
        }

        private List<UnityEngine.Object> GetCompatibleTargetsForReference(ReferenceUsageMappingItem item)
        {
            List<UnityEngine.Object> targets = new();

            foreach (UnityEngine.Object candidate in currentTargetObjectsByKey.Values)
            {
                if (candidate == null) continue;

                if (IsCompatibleReferenceTarget(item.sourceObject, candidate)) targets.Add(candidate);
            }

            targets.Sort((a, b) =>
            {
                string aLabel = BuildTargetObjectLabel(a);
                string bLabel = BuildTargetObjectLabel(b);
                return string.CompareOrdinal(aLabel, bLabel);
            });

            return targets;
        }

        private List<Component> GetCompatibleTargetComponentsForCopy(Component sourceComponent)
        {
            List<Component> targets = new();

            foreach (UnityEngine.Object value in currentTargetObjectsByKey.Values)
            {
                if (value is not Component targetComponent) continue;

                if (!IsEligibleTargetComponentForCopy(targetComponent)) continue;

                if (IsCompatibleComponentCopyTarget(sourceComponent, targetComponent)) targets.Add(targetComponent);
            }

            targets.Sort((a, b) =>
            {
                string aLabel = BuildTargetObjectLabel(a);
                string bLabel = BuildTargetObjectLabel(b);
                return string.CompareOrdinal(aLabel, bLabel);
            });

            return targets;
        }

        private bool IsEligibleSourceComponentForCopy(Component component)
        {
            if (component == null) return false;

            if (component is not Transform) return true;

            return sourceRoot != null && component == sourceRoot.transform;
        }

        private bool IsEligibleTargetComponentForCopy(Component component)
        {
            if (component == null) return false;

            if (component is not Transform) return true;

            return targetRoot != null && component == targetRoot.transform;
        }

        private bool IsCompatibleReferenceTarget(UnityEngine.Object source, UnityEngine.Object target)
        {
            if (source is GameObject) return target is GameObject;

            if (source is Component sourceComponent)
            {
                if (target is not Component targetComponent) return false;

                Type sourceType = sourceComponent.GetType();
                Type targetType = targetComponent.GetType();

                return sourceType.IsAssignableFrom(targetType) || targetType.IsAssignableFrom(sourceType);
            }

            return false;
        }

        private bool IsCompatibleComponentCopyTarget(Component source, Component target)
        {
            Type sourceType = source.GetType();
            Type targetType = target.GetType();

            return sourceType.IsAssignableFrom(targetType) || targetType.IsAssignableFrom(sourceType);
        }

        private string[] BuildReferenceTargetOptions(List<UnityEngine.Object> compatibleTargets)
        {
            string[] options = new string[compatibleTargets.Count + 1];
            options[0] = "<Keep unchanged>";

            for (int i = 0; i < compatibleTargets.Count; i++)
            {
                options[i + 1] = BuildTargetObjectLabel(compatibleTargets[i]);
            }

            return options;
        }

        private string[] BuildComponentTargetOptions(List<Component> compatibleTargets)
        {
            string[] options = new string[compatibleTargets.Count + 1];
            options[0] = "<Do not copy>";

            for (int i = 0; i < compatibleTargets.Count; i++)
            {
                options[i + 1] = BuildTargetObjectLabel(compatibleTargets[i]);
            }

            return options;
        }

        private string BuildTargetObjectLabel(UnityEngine.Object obj)
        {
            if (obj == null) return "<null>";

            if (obj is GameObject go) return $"{GetRelativePath(targetRoot.transform, go.transform)} [GameObject]";

            if (obj is Component component)
            {
                string path = GetRelativePath(targetRoot.transform, component.transform);
                if (string.IsNullOrEmpty(path)) path = "<Root>";

                int index = GetComponentIndex(component);
                return $"{path} [{component.GetType().Name} #{index}]";
            }

            return obj.name;
        }

        private void ApplyTemplateToCurrentMappings()
        {
            if (templateAsset == null || targetRoot == null || sourceRoot == null) return;

            ApplyReferenceTemplate();
            ApplyComponentCopyTemplate();
            RefreshAvailableSourceComponentsForSelection();
            Repaint();
        }

        private void ApplyReferenceTemplate()
        {
            Dictionary<string, ReferenceTransferMappingEntry> templateBySourceKey = new();

            if (templateAsset.referenceEntries != null)
            {
                foreach (ReferenceTransferMappingEntry entry in templateAsset.referenceEntries)
                {
                    if (entry == null || entry.source == null || entry.target == null) continue;

                    templateBySourceKey[entry.source.ToStableKey()] = entry;
                }
            }

            foreach (ReferenceUsageMappingItem item in referenceMappingItems)
            {
                string sourceKey = item.sourceDescriptor.ToStableKey();

                if (!templateBySourceKey.TryGetValue(sourceKey, out ReferenceTransferMappingEntry entry))
                {
                    item.mappedTarget = null;
                    continue;
                }

                currentTargetObjectsByKey.TryGetValue(entry.target.ToStableKey(), out UnityEngine.Object target);
                item.mappedTarget = target;
            }
        }

        private void ApplyComponentCopyTemplate()
        {
            Dictionary<string, ComponentCopyMappingEntry> entriesBySourceKey = new();

            if (templateAsset.componentCopyEntries != null)
            {
                foreach (ComponentCopyMappingEntry entry in templateAsset.componentCopyEntries)
                {
                    if (entry == null || entry.source == null || entry.target == null) continue;

                    entriesBySourceKey[entry.source.ToStableKey()] = entry;
                }
            }

            List<ComponentCopyMappingItem> rebuiltItems = new();

            foreach (ComponentCopyMappingEntry entry in entriesBySourceKey.Values)
            {
                if (!currentSourceObjectsByKey.TryGetValue(entry.source.ToStableKey(),
                        out UnityEngine.Object sourceObject) ||
                    sourceObject is not Component sourceComponent ||
                    !IsEligibleSourceComponentForCopy(sourceComponent))
                    continue;

                if (!TryBuildDescriptorForSource(sourceComponent,
                        out ReferenceTransferObjectDescriptor sourceDescriptor))
                    continue;

                Component mappedTarget = null;
                if (currentTargetObjectsByKey.TryGetValue(entry.target.ToStableKey(),
                        out UnityEngine.Object targetObject) &&
                    targetObject is Component targetComponent &&
                    IsEligibleTargetComponentForCopy(targetComponent))
                    mappedTarget = targetComponent;

                rebuiltItems.Add(new ComponentCopyMappingItem
                {
                    sourceComponent = sourceComponent,
                    sourceDescriptor = sourceDescriptor,
                    mappedTarget = mappedTarget
                });
            }

            componentCopyMappingItems = rebuiltItems
                .OrderBy(x => x.sourceDescriptor.relativePath, StringComparer.Ordinal)
                .ThenBy(x => x.sourceComponent.GetType().Name, StringComparer.Ordinal)
                .ThenBy(x => x.sourceDescriptor.componentIndex)
                .ToList();
        }

        private void ApplyTemplateToComponentCopyItem(ComponentCopyMappingItem item)
        {
            if (item == null || item.sourceDescriptor == null || templateAsset == null) return;

            ComponentCopyMappingEntry matchingEntry = null;

            if (templateAsset.componentCopyEntries != null)
            {
                string sourceKey = item.sourceDescriptor.ToStableKey();

                foreach (ComponentCopyMappingEntry entry in templateAsset.componentCopyEntries)
                {
                    if (entry == null || entry.source == null || entry.target == null) continue;

                    if (entry.source.ToStableKey() == sourceKey) matchingEntry = entry;
                }
            }

            if (matchingEntry == null) return;

            if (currentTargetObjectsByKey.TryGetValue(matchingEntry.target.ToStableKey(),
                    out UnityEngine.Object targetObject) &&
                targetObject is Component targetComponent &&
                IsEligibleTargetComponentForCopy(targetComponent))
                item.mappedTarget = targetComponent;
        }

        private void CreateTemplateAssetFromCurrentMappings()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Reference Transfer Template",
                TemplateAssetDefaultName,
                "asset",
                "Choose where to save the template asset.");

            if (string.IsNullOrEmpty(path)) return;

            ReferenceTransferTemplate asset = CreateInstance<ReferenceTransferTemplate>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            templateAsset = asset;
            SaveCurrentMappingsIntoTemplate();

            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }

        private void SaveCurrentMappingsIntoTemplate()
        {
            if (templateAsset == null) return;

            Undo.RecordObject(templateAsset, "Save Reference Transfer Template");

            templateAsset.referenceEntries = new List<ReferenceTransferMappingEntry>();
            templateAsset.componentCopyEntries = new List<ComponentCopyMappingEntry>();

            foreach (ReferenceUsageMappingItem item in referenceMappingItems)
            {
                if (item.mappedTarget == null) continue;

                if (!TryBuildDescriptorForTarget(item.mappedTarget,
                        out ReferenceTransferObjectDescriptor targetDescriptor))
                    continue;

                templateAsset.referenceEntries.Add(new ReferenceTransferMappingEntry
                {
                    source = CloneDescriptor(item.sourceDescriptor),
                    target = targetDescriptor
                });
            }

            foreach (ComponentCopyMappingItem item in componentCopyMappingItems)
            {
                if (item.mappedTarget == null) continue;

                if (!TryBuildDescriptorForTarget(item.mappedTarget,
                        out ReferenceTransferObjectDescriptor targetDescriptor))
                    continue;

                templateAsset.componentCopyEntries.Add(new ComponentCopyMappingEntry
                {
                    source = CloneDescriptor(item.sourceDescriptor),
                    target = targetDescriptor
                });
            }

            EditorUtility.SetDirty(templateAsset);
            AssetDatabase.SaveAssets();
        }

        private bool TryBuildDescriptorForTarget(UnityEngine.Object target,
            out ReferenceTransferObjectDescriptor descriptor)
        {
            descriptor = null;

            if (targetRoot == null || target == null) return false;

            if (target is GameObject go)
            {
                if (!go.transform.IsChildOf(targetRoot.transform) && go != targetRoot) return false;

                descriptor = new ReferenceTransferObjectDescriptor
                {
                    kind = ReferenceTransferObjectKind.GameObject,
                    relativePath = GetRelativePath(targetRoot.transform, go.transform),
                    stablePath = GetStableRelativePath(targetRoot.transform, go.transform),
                    componentTypeName = string.Empty,
                    componentIndex = 0
                };

                return true;
            }

            if (target is Component component)
            {
                if (!component.transform.IsChildOf(targetRoot.transform) && component.gameObject != targetRoot)
                    return false;

                descriptor = new ReferenceTransferObjectDescriptor
                {
                    kind = ReferenceTransferObjectKind.Component,
                    relativePath = GetRelativePath(targetRoot.transform, component.transform),
                    stablePath = GetStableRelativePath(targetRoot.transform, component.transform),
                    componentTypeName = component.GetType().AssemblyQualifiedName,
                    componentIndex = GetComponentIndex(component)
                };

                return true;
            }

            return false;
        }

        private void ExecuteTransfer()
        {
            Dictionary<UnityEngine.Object, UnityEngine.Object> remapLookup = BuildRemapLookup();

            int copiedComponentCount = 0;
            int changedReferenceCount = 0;

            foreach (ComponentCopyMappingItem item in componentCopyMappingItems)
            {
                if (item.sourceComponent == null || item.mappedTarget == null) continue;

                if (!IsCompatibleComponentCopyTarget(item.sourceComponent, item.mappedTarget)) continue;

                Undo.RecordObject(item.mappedTarget, "Copy Component Serialized Values");

                if (CopySerializedValues(item.sourceComponent, item.mappedTarget, remapLookup))
                {
                    EditorUtility.SetDirty(item.mappedTarget);
                    copiedComponentCount++;
                }
            }

            foreach (ReferenceUsageMappingItem item in referenceMappingItems)
            {
                if (item.mappedTarget == null) continue;

                foreach (ReferenceUsage usage in item.usages)
                {
                    if (usage.ownerComponent == null) continue;

                    SerializedObject ownerSerializedObject;

                    try
                    {
                        ownerSerializedObject = new SerializedObject(usage.ownerComponent);
                    }
                    catch
                    {
                        continue;
                    }

                    SerializedProperty property = ownerSerializedObject.FindProperty(usage.propertyPath);
                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference) continue;

                    if (property.objectReferenceValue == item.mappedTarget) continue;

                    Undo.RecordObject(usage.ownerComponent, "Transfer References");
                    property.objectReferenceValue = item.mappedTarget;
                    ownerSerializedObject.ApplyModifiedProperties();

                    EditorUtility.SetDirty(usage.ownerComponent);
                    changedReferenceCount++;
                }
            }

            if (copiedComponentCount > 0 || changedReferenceCount > 0)
                if (PrefabStageUtility.GetCurrentPrefabStage() == null)
                    EditorSceneManager.MarkAllScenesDirty();

            Debug.Log(
                $"Reference Transfer complete. Copied {copiedComponentCount} component mapping(s). Changed {changedReferenceCount} serialized reference(s).");

            RebuildScan();
        }

        private Dictionary<UnityEngine.Object, UnityEngine.Object> BuildRemapLookup()
        {
            Dictionary<UnityEngine.Object, UnityEngine.Object> remap = new();

            foreach (ReferenceUsageMappingItem item in referenceMappingItems)
            {
                if (item.sourceObject != null && item.mappedTarget != null)
                    remap[item.sourceObject] = item.mappedTarget;
            }

            foreach (ComponentCopyMappingItem item in componentCopyMappingItems)
            {
                if (item.sourceComponent != null && item.mappedTarget != null)
                    remap[item.sourceComponent] = item.mappedTarget;
            }

            return remap;
        }

        private static bool CopySerializedValues(
            Component source,
            Component destination,
            Dictionary<UnityEngine.Object, UnityEngine.Object> remapLookup)
        {
            if (source is Transform sourceTransform && destination is Transform destinationTransform)
                return CopyTransformValues(sourceTransform, destinationTransform);

            SerializedObject sourceSerializedObject;
            SerializedObject destinationSerializedObject;

            try
            {
                sourceSerializedObject = new SerializedObject(source);
                destinationSerializedObject = new SerializedObject(destination);
            }
            catch
            {
                return false;
            }

            bool changed = false;

            SerializedProperty sourceIterator = sourceSerializedObject.GetIterator();
            bool enterChildren = true;

            while (sourceIterator.NextVisible(enterChildren))
            {
                enterChildren = true;

                if (sourceIterator.propertyPath == "m_Script") continue;

                SerializedProperty destinationProperty =
                    destinationSerializedObject.FindProperty(sourceIterator.propertyPath);
                if (destinationProperty == null) continue;

                if (!destinationProperty.editable) continue;

                if (sourceIterator.isArray &&
                    sourceIterator.propertyType == SerializedPropertyType.Generic &&
                    sourceIterator.propertyPath != "Array")
                {
                    if (destinationProperty.isArray &&
                        destinationProperty.propertyType == SerializedPropertyType.Generic &&
                        destinationProperty.arraySize != sourceIterator.arraySize)
                    {
                        destinationProperty.arraySize = sourceIterator.arraySize;
                        changed = true;
                    }

                    continue;
                }

                if (sourceIterator.propertyType != destinationProperty.propertyType) continue;

                if (CopyPropertyValue(sourceIterator, destinationProperty, remapLookup)) changed = true;
            }

            if (changed) destinationSerializedObject.ApplyModifiedPropertiesWithoutUndo();

            return changed;
        }

        private static bool CopyTransformValues(Transform source, Transform destination)
        {
            bool changed = false;

            if (destination.localPosition != source.localPosition)
            {
                destination.localPosition = source.localPosition;
                changed = true;
            }

            if (destination.localRotation != source.localRotation)
            {
                destination.localRotation = source.localRotation;
                changed = true;
            }

            if (destination.localScale != source.localScale)
            {
                destination.localScale = source.localScale;
                changed = true;
            }

            if (source is RectTransform sourceRect && destination is RectTransform destinationRect)
            {
                if (destinationRect.anchorMin != sourceRect.anchorMin)
                {
                    destinationRect.anchorMin = sourceRect.anchorMin;
                    changed = true;
                }

                if (destinationRect.anchorMax != sourceRect.anchorMax)
                {
                    destinationRect.anchorMax = sourceRect.anchorMax;
                    changed = true;
                }

                if (destinationRect.anchoredPosition3D != sourceRect.anchoredPosition3D)
                {
                    destinationRect.anchoredPosition3D = sourceRect.anchoredPosition3D;
                    changed = true;
                }

                if (destinationRect.sizeDelta != sourceRect.sizeDelta)
                {
                    destinationRect.sizeDelta = sourceRect.sizeDelta;
                    changed = true;
                }

                if (destinationRect.pivot != sourceRect.pivot)
                {
                    destinationRect.pivot = sourceRect.pivot;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool CopyPropertyValue(
            SerializedProperty sourceProperty,
            SerializedProperty destinationProperty,
            Dictionary<UnityEngine.Object, UnityEngine.Object> remapLookup)
        {
            switch (sourceProperty.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (destinationProperty.intValue != sourceProperty.intValue)
                    {
                        destinationProperty.intValue = sourceProperty.intValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Boolean:
                    if (destinationProperty.boolValue != sourceProperty.boolValue)
                    {
                        destinationProperty.boolValue = sourceProperty.boolValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Float:
                    if (!Mathf.Approximately(destinationProperty.floatValue, sourceProperty.floatValue))
                    {
                        destinationProperty.floatValue = sourceProperty.floatValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.String:
                    if (destinationProperty.stringValue != sourceProperty.stringValue)
                    {
                        destinationProperty.stringValue = sourceProperty.stringValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Color:
                    if (destinationProperty.colorValue != sourceProperty.colorValue)
                    {
                        destinationProperty.colorValue = sourceProperty.colorValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.ObjectReference:
                {
                    UnityEngine.Object sourceReference = sourceProperty.objectReferenceValue;
                    UnityEngine.Object remappedReference = sourceReference;

                    if (sourceReference != null &&
                        remapLookup != null &&
                        remapLookup.TryGetValue(sourceReference, out UnityEngine.Object mapped))
                        remappedReference = mapped;

                    if (destinationProperty.objectReferenceValue != remappedReference)
                    {
                        destinationProperty.objectReferenceValue = remappedReference;
                        return true;
                    }
                }
                    break;

                case SerializedPropertyType.LayerMask:
                    if (destinationProperty.intValue != sourceProperty.intValue)
                    {
                        destinationProperty.intValue = sourceProperty.intValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Enum:
                    if (destinationProperty.enumValueIndex != sourceProperty.enumValueIndex)
                    {
                        destinationProperty.enumValueIndex = sourceProperty.enumValueIndex;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Vector2:
                    if (destinationProperty.vector2Value != sourceProperty.vector2Value)
                    {
                        destinationProperty.vector2Value = sourceProperty.vector2Value;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Vector3:
                    if (destinationProperty.vector3Value != sourceProperty.vector3Value)
                    {
                        destinationProperty.vector3Value = sourceProperty.vector3Value;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Vector4:
                    if (destinationProperty.vector4Value != sourceProperty.vector4Value)
                    {
                        destinationProperty.vector4Value = sourceProperty.vector4Value;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Rect:
                    if (destinationProperty.rectValue != sourceProperty.rectValue)
                    {
                        destinationProperty.rectValue = sourceProperty.rectValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.ArraySize:
                    if (destinationProperty.intValue != sourceProperty.intValue)
                    {
                        destinationProperty.intValue = sourceProperty.intValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Character:
                    if (destinationProperty.intValue != sourceProperty.intValue)
                    {
                        destinationProperty.intValue = sourceProperty.intValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.AnimationCurve:
                    if (!AnimationCurveEquals(destinationProperty.animationCurveValue,
                            sourceProperty.animationCurveValue))
                    {
                        destinationProperty.animationCurveValue = CloneCurve(sourceProperty.animationCurveValue);
                        return true;
                    }

                    break;

                case SerializedPropertyType.Bounds:
                    if (destinationProperty.boundsValue != sourceProperty.boundsValue)
                    {
                        destinationProperty.boundsValue = sourceProperty.boundsValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Gradient:
                    destinationProperty.gradientValue = sourceProperty.gradientValue;
                    return true;

                case SerializedPropertyType.Quaternion:
                    if (destinationProperty.quaternionValue != sourceProperty.quaternionValue)
                    {
                        destinationProperty.quaternionValue = sourceProperty.quaternionValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.ExposedReference:
                    if (destinationProperty.exposedReferenceValue != sourceProperty.exposedReferenceValue)
                    {
                        destinationProperty.exposedReferenceValue = sourceProperty.exposedReferenceValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.FixedBufferSize:
                    break;

                case SerializedPropertyType.Vector2Int:
                    if (destinationProperty.vector2IntValue != sourceProperty.vector2IntValue)
                    {
                        destinationProperty.vector2IntValue = sourceProperty.vector2IntValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Vector3Int:
                    if (destinationProperty.vector3IntValue != sourceProperty.vector3IntValue)
                    {
                        destinationProperty.vector3IntValue = sourceProperty.vector3IntValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.RectInt:
                    if (destinationProperty.rectIntValue != sourceProperty.rectIntValue)
                    {
                        destinationProperty.rectIntValue = sourceProperty.rectIntValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.BoundsInt:
                    if (destinationProperty.boundsIntValue != sourceProperty.boundsIntValue)
                    {
                        destinationProperty.boundsIntValue = sourceProperty.boundsIntValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.ManagedReference:
                    if (!ReferenceEquals(destinationProperty.managedReferenceValue,
                            sourceProperty.managedReferenceValue))
                    {
                        destinationProperty.managedReferenceValue = sourceProperty.managedReferenceValue;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Hash128:
                    if (destinationProperty.hash128Value != sourceProperty.hash128Value)
                    {
                        destinationProperty.hash128Value = sourceProperty.hash128Value;
                        return true;
                    }

                    break;

                case SerializedPropertyType.Generic:
                default:
                    break;
            }

            return false;
        }

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null) return null;

            return new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode
            };
        }

        private static bool AnimationCurveEquals(AnimationCurve a, AnimationCurve b)
        {
            if (ReferenceEquals(a, b)) return true;

            if (a == null || b == null) return false;

            if (a.length != b.length) return false;

            if (a.preWrapMode != b.preWrapMode || a.postWrapMode != b.postWrapMode) return false;

            for (int i = 0; i < a.length; i++)
            {
                Keyframe ka = a.keys[i];
                Keyframe kb = b.keys[i];

                if (ka.time != kb.time ||
                    ka.value != kb.value ||
                    ka.inTangent != kb.inTangent ||
                    ka.outTangent != kb.outTangent ||
                    ka.inWeight != kb.inWeight ||
                    ka.outWeight != kb.outWeight ||
                    ka.weightedMode != kb.weightedMode)
                    return false;
            }

            return true;
        }

        private static ReferenceTransferObjectDescriptor CloneDescriptor(ReferenceTransferObjectDescriptor descriptor)
        {
            return new ReferenceTransferObjectDescriptor
            {
                kind = descriptor.kind,
                relativePath = descriptor.relativePath,
                stablePath = descriptor.stablePath,
                componentTypeName = descriptor.componentTypeName,
                componentIndex = descriptor.componentIndex
            };
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            List<string> names = new();
            Transform current = gameObject.transform;

            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static string GetRelativePath(Transform root, Transform current)
        {
            if (current == root) return string.Empty;

            List<string> parts = new();
            Transform iterator = current;

            while (iterator != null && iterator != root)
            {
                parts.Add(iterator.name);
                iterator = iterator.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string GetStableRelativePath(Transform root, Transform current)
        {
            if (current == root) return string.Empty;

            List<string> parts = new();
            Transform iterator = current;

            while (iterator != null && iterator != root)
            {
                parts.Add($"{iterator.name}[{GetSiblingNameIndex(iterator)}]");
                iterator = iterator.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static int GetSiblingNameIndex(Transform transform)
        {
            int index = 0;
            Transform parent = transform.parent;
            int siblingIndex = transform.GetSiblingIndex();

            if (parent == null) return 0;

            for (int i = 0; i < siblingIndex; i++)
            {
                if (parent.GetChild(i).name == transform.name) index++;
            }

            return index;
        }

        private static int GetComponentIndex(Component component)
        {
            Component[] all = component.GetComponents(component.GetType());
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == component) return i;
            }

            return 0;
        }

        [Serializable]
        private sealed class ReferenceUsageMappingItem
        {
            public UnityEngine.Object sourceObject;
            public ReferenceTransferObjectDescriptor sourceDescriptor;
            public UnityEngine.Object mappedTarget;
            public List<ReferenceUsage> usages;
        }

        [Serializable]
        private sealed class ComponentCopyMappingItem
        {
            public Component sourceComponent;
            public ReferenceTransferObjectDescriptor sourceDescriptor;
            public Component mappedTarget;
        }

        [Serializable]
        private sealed class ReferenceUsage
        {
            public Component ownerComponent;
            public string propertyPath;
            public string ownerHierarchyPath;
        }
    }

    internal static class ReferenceTransferTypeUtility
    {
        private static readonly Dictionary<string, Type> Cache = new();

        public static Type FindType(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName)) return null;

            if (Cache.TryGetValue(assemblyQualifiedName, out Type cached)) return cached;

            Type found = Type.GetType(assemblyQualifiedName);
            if (found != null)
            {
                Cache[assemblyQualifiedName] = found;
                return found;
            }

            string fullTypeName = ExtractFullTypeName(assemblyQualifiedName);

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                found = assembly.GetType(fullTypeName);
                if (found != null)
                {
                    Cache[assemblyQualifiedName] = found;
                    return found;
                }
            }

            Cache[assemblyQualifiedName] = null;
            return null;
        }

        private static string ExtractFullTypeName(string assemblyQualifiedName)
        {
            int commaIndex = assemblyQualifiedName.IndexOf(',');
            return commaIndex >= 0
                ? assemblyQualifiedName.Substring(0, commaIndex).Trim()
                : assemblyQualifiedName.Trim();
        }
    }
}
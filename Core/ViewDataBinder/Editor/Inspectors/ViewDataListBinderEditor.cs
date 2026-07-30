using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryTools.ViewBinding.Editor
{
    [CustomEditor(typeof(ViewDataListBinder))]
    public sealed class ViewDataListBinderEditor : UnityEditor.Editor
    {
        private SerializedProperty source;
        private SerializedProperty updateTiming;
        private SerializedProperty errorPolicy;
        private SerializedProperty targetParent;
        private SerializedProperty defaultPrefab;
        private SerializedProperty surplusPolicy;
        private SerializedProperty reservedKeyCapacity;
        private SerializedProperty offset;
        private SerializedProperty rangeCount;
        private SerializedProperty maxItems;
        private SerializedProperty keyEndpoint;
        private SerializedProperty filter;
        private SerializedProperty sorting;
        private SerializedProperty prefabRules;
        private SerializedProperty creationMode;
        private SerializedProperty itemsPerFrame;
        private SerializedProperty frameBudget;
        private SerializedProperty virtualization;
        private SerializedProperty refreshStarted;
        private SerializedProperty refreshCompleted;
        private SerializedProperty itemCreated;
        private SerializedProperty itemBound;
        private SerializedProperty itemUnbound;
        private SerializedProperty itemVisibilityChanged;
        private SerializedProperty itemDeactivated;
        private SerializedProperty itemDestroyed;

        private void OnEnable()
        {
            source = serializedObject.FindProperty("source");
            updateTiming = serializedObject.FindProperty("updateTiming");
            errorPolicy = serializedObject.FindProperty("errorPolicy");
            targetParent = serializedObject.FindProperty("targetParent");
            defaultPrefab = serializedObject.FindProperty("defaultPrefab");
            surplusPolicy = serializedObject.FindProperty("surplusPolicy");
            reservedKeyCapacity = serializedObject.FindProperty("reservedKeyCapacity");
            offset = serializedObject.FindProperty("offset");
            rangeCount = serializedObject.FindProperty("rangeCount");
            maxItems = serializedObject.FindProperty("maxItems");
            keyEndpoint = serializedObject.FindProperty("keyEndpoint");
            filter = serializedObject.FindProperty("filter");
            sorting = serializedObject.FindProperty("sorting");
            prefabRules = serializedObject.FindProperty("prefabRules");
            creationMode = serializedObject.FindProperty("creationMode");
            itemsPerFrame = serializedObject.FindProperty("itemsPerFrame");
            frameBudget = serializedObject.FindProperty("frameBudgetMilliseconds");
            virtualization = serializedObject.FindProperty("virtualization");
            refreshStarted = serializedObject.FindProperty("refreshStarted");
            refreshCompleted = serializedObject.FindProperty("refreshCompleted");
            itemCreated = serializedObject.FindProperty("itemCreated");
            itemBound = serializedObject.FindProperty("itemBound");
            itemUnbound = serializedObject.FindProperty("itemUnbound");
            itemVisibilityChanged = serializedObject.FindProperty("itemVisibilityChanged");
            itemDeactivated = serializedObject.FindProperty("itemDeactivated");
            itemDestroyed = serializedObject.FindProperty("itemDestroyed");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeader("Collection Source");
            BindingEndpointInspectorUtility.DrawEndpoint(
                serializedObject,
                source,
                true,
                false,
                "Select IEnumerable Member");
            ValidateSource();
            EditorGUILayout.PropertyField(updateTiming);
            EditorGUILayout.PropertyField(errorPolicy);

            DrawHeader("Item Container & Template");
            EditorGUILayout.PropertyField(targetParent);
            EditorGUILayout.PropertyField(defaultPrefab);
            EditorGUILayout.PropertyField(surplusPolicy);
            if ((ViewDataListSurplusPolicy)surplusPolicy.enumValueIndex ==
                ViewDataListSurplusPolicy.Deactivate)
            {
                EditorGUILayout.PropertyField(
                    reservedKeyCapacity,
                    new GUIContent("Reserved Key Capacity"));
            }
            ValidateTemplate();

            DrawHeader("Projection");
            EditorGUILayout.PropertyField(offset);
            EditorGUILayout.PropertyField(rangeCount);
            EditorGUILayout.PropertyField(maxItems);
            EditorGUILayout.LabelField("Stable Key", EditorStyles.boldLabel);
            BindingEndpointInspectorUtility.DrawEndpoint(
                serializedObject,
                keyEndpoint,
                true,
                false,
                "Select Key Member");
            ValidateKey();

            DrawPredicate(filter, "Filter");
            DrawSorting();
            DrawPrefabRules();

            DrawHeader("Creation");
            EditorGUILayout.PropertyField(creationMode);
            if ((ViewDataListCreationMode)creationMode.enumValueIndex ==
                ViewDataListCreationMode.Batched)
            {
                EditorGUILayout.PropertyField(itemsPerFrame);
                EditorGUILayout.PropertyField(frameBudget);
            }

            DrawHeader("Virtualization");
            EditorGUILayout.PropertyField(virtualization, true);
            ValidateVirtualization();

            DrawHeader("Lifecycle");
            EditorGUILayout.PropertyField(refreshStarted);
            EditorGUILayout.PropertyField(refreshCompleted);
            EditorGUILayout.PropertyField(itemCreated);
            EditorGUILayout.PropertyField(itemBound);
            EditorGUILayout.PropertyField(itemUnbound);
            EditorGUILayout.PropertyField(itemVisibilityChanged);
            EditorGUILayout.PropertyField(itemDeactivated);
            EditorGUILayout.PropertyField(itemDestroyed);

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Refresh"))
                    {
                        ((ViewDataListBinder)target).Refresh();
                    }
                    if (GUILayout.Button("Refresh Immediate"))
                    {
                        ((ViewDataListBinder)target).RefreshImmediate();
                    }
                }

                ViewDataListBinder binder = (ViewDataListBinder)target;
                EditorGUILayout.LabelField(
                    $"Projected: {binder.ProjectedCount}  Active: {binder.ActiveItems.Count}",
                    EditorStyles.miniLabel);
                if (binder.LastResult != null &&
                    !string.IsNullOrWhiteSpace(binder.LastResult.Message))
                {
                    EditorGUILayout.HelpBox(
                        binder.LastResult.Message,
                        binder.LastResult.IsSuccess ? MessageType.Info : MessageType.Warning);
                }
            }
        }

        private void DrawPredicate(SerializedProperty predicate, string label)
        {
            EditorGUILayout.Space(3f);
            predicate.isExpanded = EditorGUILayout.Foldout(predicate.isExpanded, label, true);
            if (!predicate.isExpanded)
            {
                return;
            }
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(predicate.FindPropertyRelative("enabled"));
            if (!predicate.FindPropertyRelative("enabled").boolValue)
            {
                EditorGUI.indentLevel--;
                return;
            }
            SerializedProperty sources = predicate.FindPropertyRelative("sources");
            DrawSourceList(sources);
            SerializedProperty condition = predicate.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(condition.FindPropertyRelative("name"));
            EditorGUILayout.PropertyField(condition.FindPropertyRelative("enabled"));
            if (condition.FindPropertyRelative("enabled").boolValue)
            {
                BindingConditionInspectorUtility.DrawExpression(
                    condition.FindPropertyRelative("clauses"),
                    sources);
                BindingConditionInspectorUtility.DrawValidation(
                    condition.FindPropertyRelative("clauses"),
                    sources);
            }
            EditorGUI.indentLevel--;
        }

        private void DrawSourceList(SerializedProperty sources)
        {
            for (int i = 0; i < sources.arraySize; i++)
            {
                SerializedProperty sourceProperty = sources.GetArrayElementAtIndex(i);
                EditorGUILayout.LabelField($"Source {i + 1}", EditorStyles.miniBoldLabel);
                BindingEndpointInspectorUtility.DrawEndpoint(
                    serializedObject,
                    sourceProperty.FindPropertyRelative("endpoint"),
                    true,
                    false,
                    "Select Item Member");
                if (GUILayout.Button("Remove Source", EditorStyles.miniButton))
                {
                    sources.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
            if (GUILayout.Button("Add Source", EditorStyles.miniButton))
            {
                int index = sources.arraySize++;
                BindingEndpointInspectorUtility.ResetEndpoint(
                    sources.GetArrayElementAtIndex(index).FindPropertyRelative("endpoint"));
            }
        }

        private void DrawSorting()
        {
            sorting.isExpanded = EditorGUILayout.Foldout(sorting.isExpanded, "Sorting", true);
            if (!sorting.isExpanded)
            {
                return;
            }
            EditorGUI.indentLevel++;
            for (int i = 0; i < sorting.arraySize; i++)
            {
                SerializedProperty descriptor = sorting.GetArrayElementAtIndex(i);
                EditorGUILayout.LabelField($"Sort {i + 1}", EditorStyles.miniBoldLabel);
                BindingEndpointInspectorUtility.DrawEndpoint(
                    serializedObject,
                    descriptor.FindPropertyRelative("endpoint"),
                    true,
                    false,
                    "Select Sort Member");
                EditorGUILayout.PropertyField(descriptor.FindPropertyRelative("direction"));
                EditorGUILayout.PropertyField(descriptor.FindPropertyRelative("nullOrder"));
                if (GUILayout.Button("Remove Sort", EditorStyles.miniButton))
                {
                    sorting.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
            if (GUILayout.Button("Add Sort", EditorStyles.miniButton))
            {
                int index = sorting.arraySize++;
                SerializedProperty descriptor = sorting.GetArrayElementAtIndex(index);
                BindingEndpointInspectorUtility.ResetEndpoint(
                    descriptor.FindPropertyRelative("endpoint"));
                descriptor.FindPropertyRelative("direction").enumValueIndex =
                    (int)ViewDataListSortDirection.Ascending;
                descriptor.FindPropertyRelative("nullOrder").enumValueIndex =
                    (int)ViewDataListNullOrder.Last;
            }
            EditorGUI.indentLevel--;
        }

        private void DrawPrefabRules()
        {
            prefabRules.isExpanded = EditorGUILayout.Foldout(prefabRules.isExpanded, "Prefab Rules", true);
            if (!prefabRules.isExpanded)
            {
                return;
            }
            EditorGUI.indentLevel++;
            for (int i = 0; i < prefabRules.arraySize; i++)
            {
                SerializedProperty rule = prefabRules.GetArrayElementAtIndex(i);
                EditorGUILayout.PropertyField(rule.FindPropertyRelative("name"));
                EditorGUILayout.PropertyField(rule.FindPropertyRelative("enabled"));
                SerializedProperty rulePrefab = rule.FindPropertyRelative("prefab");
                EditorGUILayout.PropertyField(rulePrefab);
                if (rulePrefab.objectReferenceValue is GameObject prefab)
                {
                    ValidateTemplateLocation(prefab);
                    ValidatePrefabContract(prefab);
                }
                DrawPredicate(rule.FindPropertyRelative("predicate"), "Condition");
                if (GUILayout.Button("Remove Rule", EditorStyles.miniButton))
                {
                    prefabRules.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.Space(4f);
            }
            if (GUILayout.Button("Add Prefab Rule", EditorStyles.miniButton))
            {
                int index = prefabRules.arraySize++;
                ResetPrefabRule(prefabRules.GetArrayElementAtIndex(index));
            }
            EditorGUI.indentLevel--;
        }

        private static void ResetPrefabRule(SerializedProperty rule)
        {
            rule.FindPropertyRelative("name").stringValue = "Prefab Rule";
            rule.FindPropertyRelative("enabled").boolValue = true;
            rule.FindPropertyRelative("prefab").objectReferenceValue = null;
            SerializedProperty predicate = rule.FindPropertyRelative("predicate");
            predicate.FindPropertyRelative("enabled").boolValue = false;
            SerializedProperty sources = predicate.FindPropertyRelative("sources");
            sources.arraySize = 1;
            BindingEndpointInspectorUtility.ResetEndpoint(
                sources.GetArrayElementAtIndex(0).FindPropertyRelative("endpoint"));
            SerializedProperty condition = predicate.FindPropertyRelative("condition");
            condition.FindPropertyRelative("name").stringValue = "Condition";
            condition.FindPropertyRelative("enabled").boolValue = true;
            SerializedProperty clauses = condition.FindPropertyRelative("clauses");
            clauses.arraySize = 1;
            BindingConditionInspectorUtility.ResetClause(
                clauses.GetArrayElementAtIndex(0));
            condition.FindPropertyRelative("actions").arraySize = 0;
        }

        private void ValidateTemplate()
        {
            if (targetParent.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "Target Parent is required.",
                    MessageType.Error);
            }
            if (defaultPrefab.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "Configure a default prefab fallback. A runtime PrefabResolver may replace it through the API.",
                    MessageType.Warning);
            }
            else
            {
                GameObject prefab = (GameObject)defaultPrefab.objectReferenceValue;
                ValidateTemplateLocation(prefab);
                ValidatePrefabContract(prefab);
            }
        }

        private void ValidateTemplateLocation(GameObject prefab)
        {
            Transform parent = targetParent.objectReferenceValue as Transform;
            if (PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                return;
            }
            if (parent == null || !prefab.transform.IsChildOf(parent))
            {
                EditorGUILayout.HelpBox(
                    $"Scene template '{prefab.name}' must be a child of Target Parent.",
                    MessageType.Error);
            }
        }

        private void ValidateSource()
        {
            if (!BindingEditorResolver.TryGetMemberMetadata(
                    source,
                    out BindingMemberMetadata metadata,
                    out _))
            {
                return;
            }
            if (metadata.ValueType == typeof(string) ||
                !typeof(IEnumerable).IsAssignableFrom(metadata.ValueType))
            {
                EditorGUILayout.HelpBox(
                    $"The selected Source type '{metadata.ValueType.Name}' is not a supported collection.",
                    MessageType.Error);
            }
        }

        private void ValidateKey()
        {
            if (!BindingEditorResolver.TryGetMemberMetadata(
                    source,
                    out BindingMemberMetadata sourceMetadata,
                    out _) ||
                !BindingEditorResolver.TryResolveInstance(
                    keyEndpoint.FindPropertyRelative("instance"),
                    out BindingInstanceHandle keyRoot,
                    out _))
            {
                return;
            }

            Type itemType = GetEnumerableItemType(sourceMetadata.ValueType);
            if (itemType != null &&
                keyRoot.Type != null &&
                !keyRoot.Type.IsAssignableFrom(itemType) &&
                !itemType.IsAssignableFrom(keyRoot.Type))
            {
                EditorGUILayout.HelpBox(
                    $"Key endpoint type '{keyRoot.Type.Name}' is incompatible with collection item type '{itemType.Name}'.",
                    MessageType.Error);
            }
        }

        private static Type GetEnumerableItemType(Type collectionType)
        {
            if (collectionType == null || collectionType == typeof(string))
            {
                return null;
            }
            if (collectionType.IsArray)
            {
                return collectionType.GetElementType();
            }
            if (collectionType.IsGenericType &&
                collectionType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IEnumerable<>))
            {
                return collectionType.GetGenericArguments()[0];
            }
            Type[] interfaces = collectionType.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                Type candidate = interfaces[i];
                if (candidate.IsGenericType &&
                    candidate.GetGenericTypeDefinition() ==
                    typeof(System.Collections.Generic.IEnumerable<>))
                {
                    return candidate.GetGenericArguments()[0];
                }
            }
            return null;
        }

        private static void ValidatePrefabContract(GameObject prefab)
        {
            if (prefab.GetComponentInChildren<ViewDataBinder>(true) != null ||
                prefab.GetComponentInChildren<ViewDataEventBinder>(true) != null ||
                prefab.GetComponentInChildren<ViewDataListBinder>(true) != null)
            {
                return;
            }

            MonoBehaviour[] behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IViewDataListItem)
                {
                    return;
                }
            }
            EditorGUILayout.HelpBox(
                $"Prefab '{prefab.name}' has no ViewData binder or IViewDataListItem implementation.",
                MessageType.Warning);
        }

        private void ValidateVirtualization()
        {
            SerializedProperty enabled = virtualization.FindPropertyRelative("enabled");
            if (!enabled.boolValue)
            {
                return;
            }
            Transform parent = targetParent.objectReferenceValue as Transform;
            ScrollRect scrollRect = virtualization.FindPropertyRelative("scrollRect")
                .objectReferenceValue as ScrollRect;
            if (!(parent is RectTransform))
            {
                EditorGUILayout.HelpBox(
                    "Virtualization requires Target Parent to be a RectTransform.",
                    MessageType.Error);
            }
            if (scrollRect == null)
            {
                EditorGUILayout.HelpBox(
                    "Virtualization requires a ScrollRect.",
                    MessageType.Error);
            }
            else if (parent != null && scrollRect.content != parent)
            {
                EditorGUILayout.HelpBox(
                    "Target Parent must be the configured ScrollRect content.",
                    MessageType.Error);
            }
            if (parent != null && parent.GetComponent<LayoutGroup>() is LayoutGroup layout && layout.enabled)
            {
                EditorGUILayout.HelpBox(
                    "Disable the LayoutGroup while virtualization is enabled; items are positioned manually.",
                    MessageType.Warning);
            }
        }

        private static void DrawHeader(string label)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }
    }
}

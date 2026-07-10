using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    [CustomEditor(typeof(ViewDataEventBinder))]
    public sealed class ViewDataEventBinderEditor : UnityEditor.Editor
    {
        private static readonly string[] ComparisonOperatorLabels =
        {
            "==",
            "!=",
            ">",
            ">=",
            "<",
            "<=",
            "is null",
            "is not null",
            "is true",
            "is false"
        };

        private static readonly string[] LogicalOperatorLabels =
        {
            "AND",
            "OR",
            "XOR"
        };

        private readonly Dictionary<string, bool> bindingFoldouts = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> conditionFoldouts = new Dictionary<string, bool>();

        private SerializedProperty eventBindingsProperty;
        private ViewDataEventBinder binder;

        private void OnEnable()
        {
            binder = (ViewDataEventBinder)target;
            eventBindingsProperty = serializedObject.FindProperty("eventBindings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTopBar();
            EditorGUILayout.Space(6f);

            for (int i = 0; i < eventBindingsProperty.arraySize; i++)
            {
                SerializedProperty bindingProperty = eventBindingsProperty.GetArrayElementAtIndex(i);
                if (DrawEventBinding(bindingProperty, i))
                {
                    eventBindingsProperty.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("+ Add Event Binding", GUILayout.Height(26f)))
            {
                AddEventBinding();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTopBar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("View Data Event Binder", BindingInspectorStyles.HeaderStyle);
                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    {
                        if (GUILayout.Button("Process Manual", GUILayout.Width(104f)))
                        {
                            serializedObject.ApplyModifiedProperties();
                            binder.ProcessManualBindings();
                        }

                        if (GUILayout.Button("Process All", GUILayout.Width(82f)))
                        {
                            serializedObject.ApplyModifiedProperties();
                            binder.ProcessAll();
                        }
                    }
                }

                EditorGUILayout.LabelField(
                    "Observe Source members, evaluate visual conditions, and invoke serialized UnityEvents when values change.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private bool DrawEventBinding(SerializedProperty bindingProperty, int bindingIndex)
        {
            SerializedProperty idProperty = bindingProperty.FindPropertyRelative("id");
            SerializedProperty nameProperty = bindingProperty.FindPropertyRelative("name");
            SerializedProperty enabledProperty = bindingProperty.FindPropertyRelative("enabled");
            SerializedProperty updateTimingProperty = bindingProperty.FindPropertyRelative("updateTiming");
            SerializedProperty triggerOnInitializeProperty = bindingProperty.FindPropertyRelative("triggerOnInitialize");
            SerializedProperty sourcesProperty = bindingProperty.FindPropertyRelative("sources");
            SerializedProperty conditionsProperty = bindingProperty.FindPropertyRelative("conditions");

            EnsureBindingId(idProperty);
            string key = string.IsNullOrEmpty(idProperty.stringValue)
                ? bindingIndex.ToString()
                : idProperty.stringValue;

            if (!bindingFoldouts.TryGetValue(key, out bool expanded))
            {
                expanded = true;
            }

            bool remove = false;

            using (new EditorGUILayout.VerticalScope(BindingInspectorStyles.CardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect foldoutRect = GUILayoutUtility.GetRect(
                        14f,
                        EditorGUIUtility.singleLineHeight,
                        GUILayout.Width(14f));
                    expanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
                    bindingFoldouts[key] = expanded;

                    enabledProperty.boolValue = EditorGUILayout.Toggle(enabledProperty.boolValue, GUILayout.Width(18f));
                    nameProperty.stringValue = EditorGUILayout.TextField(nameProperty.stringValue, EditorStyles.boldLabel);

                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24f)))
                    {
                        remove = true;
                    }
                }

                if (!expanded)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(34f);
                        GUILayout.Label(
                            $"{updateTimingProperty.enumDisplayNames[updateTimingProperty.enumValueIndex]}  •  {sourcesProperty.arraySize} Sources  •  {conditionsProperty.arraySize} Conditions",
                            EditorStyles.miniLabel);
                    }

                    return remove;
                }

                EditorGUILayout.Space(3f);
                EditorGUILayout.PropertyField(updateTimingProperty, new GUIContent("Polling"));
                EditorGUILayout.PropertyField(
                    triggerOnInitializeProperty,
                    new GUIContent(
                        "Trigger On Initialize",
                        "Evaluate and invoke matching Conditions during the first successful observation instead of waiting for a value change."));

                EditorGUILayout.Space(7f);
                DrawSectionTitle("SOURCES");
                DrawSources(sourcesProperty, conditionsProperty);

                EditorGUILayout.Space(7f);
                DrawSectionTitle("CONDITIONS");
                DrawConditions(conditionsProperty, sourcesProperty, key);
            }

            return remove;
        }

        private void DrawSources(
            SerializedProperty sourcesProperty,
            SerializedProperty conditionsProperty)
        {
            if (sourcesProperty.arraySize == 0)
            {
                sourcesProperty.arraySize = 1;
                ResetSource(sourcesProperty.GetArrayElementAtIndex(0));
            }

            for (int i = 0; i < sourcesProperty.arraySize; i++)
            {
                SerializedProperty sourceProperty = sourcesProperty.GetArrayElementAtIndex(i);
                SerializedProperty endpointProperty = sourceProperty.FindPropertyRelative("endpoint");

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"Source {i + 1}", EditorStyles.miniBoldLabel);
                        GUILayout.FlexibleSpace();

                        using (new EditorGUI.DisabledScope(sourcesProperty.arraySize <= 1))
                        {
                            if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(58f)))
                            {
                                sourcesProperty.DeleteArrayElementAtIndex(i);
                                RemapConditionSourceIndices(
                                    conditionsProperty,
                                    i,
                                    sourcesProperty.arraySize);
                                return;
                            }
                        }
                    }

                    BindingEndpointInspectorUtility.DrawEndpoint(
                        serializedObject,
                        endpointProperty,
                        true,
                        false,
                        "Select Observed Member");
                }
            }

            if (GUILayout.Button("+ Add Source", EditorStyles.miniButton))
            {
                int index = sourcesProperty.arraySize;
                sourcesProperty.arraySize++;
                ResetSource(sourcesProperty.GetArrayElementAtIndex(index));
            }
        }

        private void DrawConditions(
            SerializedProperty conditionsProperty,
            SerializedProperty sourcesProperty,
            string bindingKey)
        {
            for (int i = 0; i < conditionsProperty.arraySize; i++)
            {
                SerializedProperty conditionProperty = conditionsProperty.GetArrayElementAtIndex(i);
                if (DrawCondition(conditionProperty, i, sourcesProperty, bindingKey))
                {
                    conditionsProperty.DeleteArrayElementAtIndex(i);
                    return;
                }
            }

            if (GUILayout.Button("+ Add Condition", EditorStyles.miniButton))
            {
                int index = conditionsProperty.arraySize;
                conditionsProperty.arraySize++;
                ResetCondition(conditionsProperty.GetArrayElementAtIndex(index));
            }
        }

        private bool DrawCondition(
            SerializedProperty conditionProperty,
            int conditionIndex,
            SerializedProperty sourcesProperty,
            string bindingKey)
        {
            SerializedProperty nameProperty = conditionProperty.FindPropertyRelative("name");
            SerializedProperty enabledProperty = conditionProperty.FindPropertyRelative("enabled");
            SerializedProperty clausesProperty = conditionProperty.FindPropertyRelative("clauses");
            SerializedProperty actionsProperty = conditionProperty.FindPropertyRelative("actions");

            string key = $"{bindingKey}:condition:{conditionIndex}";
            if (!conditionFoldouts.TryGetValue(key, out bool expanded))
            {
                expanded = true;
            }

            bool remove = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect foldoutRect = GUILayoutUtility.GetRect(
                        14f,
                        EditorGUIUtility.singleLineHeight,
                        GUILayout.Width(14f));
                    expanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, true);
                    conditionFoldouts[key] = expanded;

                    enabledProperty.boolValue = EditorGUILayout.Toggle(enabledProperty.boolValue, GUILayout.Width(18f));
                    nameProperty.stringValue = EditorGUILayout.TextField(nameProperty.stringValue, EditorStyles.boldLabel);

                    if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(58f)))
                    {
                        remove = true;
                    }
                }

                if (!expanded)
                {
                    EditorGUILayout.LabelField(
                        $"{clausesProperty.arraySize} Clauses  •  {actionsProperty.arraySize} Actions",
                        EditorStyles.miniLabel);
                    return remove;
                }

                EditorGUILayout.Space(4f);
                DrawConditionExpression(clausesProperty, sourcesProperty);

                EditorGUILayout.Space(5f);
                DrawSectionTitle("ACTIONS");
                DrawActions(actionsProperty);

                DrawConditionValidation(clausesProperty, sourcesProperty);
            }

            return remove;
        }

        private void DrawConditionExpression(
            SerializedProperty clausesProperty,
            SerializedProperty sourcesProperty)
        {
            if (clausesProperty.arraySize == 0)
            {
                clausesProperty.arraySize = 1;
                ResetClause(clausesProperty.GetArrayElementAtIndex(0));
            }

            string[] sourceLabels = BuildSourceLabels(sourcesProperty);

            for (int i = 0; i < clausesProperty.arraySize; i++)
            {
                SerializedProperty clauseProperty = clausesProperty.GetArrayElementAtIndex(i);
                SerializedProperty sourceIndexProperty = clauseProperty.FindPropertyRelative("sourceIndex");
                SerializedProperty logicalOperatorProperty = clauseProperty.FindPropertyRelative("logicalOperator");
                SerializedProperty negateProperty = clauseProperty.FindPropertyRelative("negate");
                SerializedProperty comparisonOperatorProperty = clauseProperty.FindPropertyRelative("comparisonOperator");
                SerializedProperty comparisonValueProperty = clauseProperty.FindPropertyRelative("comparisonValue");

                sourceIndexProperty.intValue = Mathf.Clamp(
                    sourceIndexProperty.intValue,
                    0,
                    Mathf.Max(0, sourcesProperty.arraySize - 1));

                if (i > 0)
                {
                    int logicalIndex = Mathf.Clamp(
                        logicalOperatorProperty.enumValueIndex,
                        0,
                        LogicalOperatorLabels.Length - 1);
                    logicalOperatorProperty.enumValueIndex = EditorGUILayout.Popup(
                        "Logical Operator",
                        logicalIndex,
                        LogicalOperatorLabels);
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"Clause {i + 1}", EditorStyles.miniBoldLabel);
                        GUILayout.FlexibleSpace();

                        negateProperty.boolValue = GUILayout.Toggle(
                            negateProperty.boolValue,
                            "NOT",
                            EditorStyles.miniButton,
                            GUILayout.Width(44f));

                        using (new EditorGUI.DisabledScope(clausesProperty.arraySize <= 1))
                        {
                            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24f)))
                            {
                                clausesProperty.DeleteArrayElementAtIndex(i);
                                return;
                            }
                        }
                    }

                    sourceIndexProperty.intValue = EditorGUILayout.Popup(
                        "Observed Source",
                        sourceIndexProperty.intValue,
                        sourceLabels);

                    int operatorIndex = Mathf.Clamp(
                        comparisonOperatorProperty.enumValueIndex,
                        0,
                        ComparisonOperatorLabels.Length - 1);
                    comparisonOperatorProperty.enumValueIndex = EditorGUILayout.Popup(
                        "Operator",
                        operatorIndex,
                        ComparisonOperatorLabels);

                    EventBindingComparisonOperator comparisonOperator =
                        (EventBindingComparisonOperator)comparisonOperatorProperty.enumValueIndex;

                    if (EventBindingConditionEvaluator.RequiresComparisonValue(comparisonOperator))
                    {
                        Type sourceType = GetSourceType(sourcesProperty, sourceIndexProperty.intValue);
                        BindingSerializedValueDrawer.Draw(
                            comparisonValueProperty,
                            sourceType,
                            "Compare With");
                    }
                }
            }

            if (GUILayout.Button("+ Add Clause", EditorStyles.miniButton))
            {
                int index = clausesProperty.arraySize;
                clausesProperty.arraySize++;
                ResetClause(clausesProperty.GetArrayElementAtIndex(index));
            }

            EditorGUILayout.LabelField(
                BuildExpressionPreview(clausesProperty, sourceLabels),
                EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawActions(SerializedProperty actionsProperty)
        {
            for (int i = 0; i < actionsProperty.arraySize; i++)
            {
                SerializedProperty actionProperty = actionsProperty.GetArrayElementAtIndex(i);
                SerializedProperty parameterModeProperty = actionProperty.FindPropertyRelative("parameterMode");

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"Action {i + 1}", EditorStyles.miniBoldLabel);
                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(58f)))
                        {
                            actionsProperty.DeleteArrayElementAtIndex(i);
                            return;
                        }
                    }

                    EditorGUILayout.PropertyField(parameterModeProperty, new GUIContent("Parameters"));

                    EventBindingActionParameterMode mode =
                        (EventBindingActionParameterMode)parameterModeProperty.enumValueIndex;

                    SerializedProperty eventProperty = GetSelectedUnityEventProperty(actionProperty, mode);
                    EditorGUILayout.PropertyField(eventProperty, new GUIContent("Unity Event"), true);
                }
            }

            if (GUILayout.Button("+ Add Action", EditorStyles.miniButton))
            {
                int index = actionsProperty.arraySize;
                actionsProperty.arraySize++;
                ResetAction(actionsProperty.GetArrayElementAtIndex(index));
            }

            if (actionsProperty.arraySize == 0)
            {
                EditorGUILayout.LabelField(
                    "This Condition has no Actions. It can evaluate to true but will not invoke anything.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static SerializedProperty GetSelectedUnityEventProperty(
            SerializedProperty actionProperty,
            EventBindingActionParameterMode mode)
        {
            switch (mode)
            {
                case EventBindingActionParameterMode.OldValue:
                    return actionProperty.FindPropertyRelative("oldValueEvent");
                case EventBindingActionParameterMode.NewValue:
                    return actionProperty.FindPropertyRelative("newValueEvent");
                case EventBindingActionParameterMode.OldAndNewValues:
                    return actionProperty.FindPropertyRelative("oldAndNewValuesEvent");
                default:
                    return actionProperty.FindPropertyRelative("eventWithoutParameters");
            }
        }

        private static void DrawConditionValidation(
            SerializedProperty clausesProperty,
            SerializedProperty sourcesProperty)
        {
            if (clausesProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox("A Condition requires at least one Clause.", MessageType.Error);
                return;
            }

            for (int i = 0; i < clausesProperty.arraySize; i++)
            {
                SerializedProperty clause = clausesProperty.GetArrayElementAtIndex(i);
                int sourceIndex = clause.FindPropertyRelative("sourceIndex").intValue;
                if (sourceIndex < 0 || sourceIndex >= sourcesProperty.arraySize)
                {
                    EditorGUILayout.HelpBox(
                        $"Clause {i + 1} references a Source that does not exist.",
                        MessageType.Error);
                    return;
                }

                SerializedProperty endpoint = sourcesProperty
                    .GetArrayElementAtIndex(sourceIndex)
                    .FindPropertyRelative("endpoint");

                if (!BindingEditorResolver.TryGetMemberMetadata(
                        endpoint,
                        out BindingMemberMetadata metadata,
                        out string error))
                {
                    EditorGUILayout.HelpBox($"Clause {i + 1}: {error}", MessageType.Warning);
                    return;
                }

                if (!metadata.CanRead)
                {
                    EditorGUILayout.HelpBox(
                        $"Clause {i + 1}: Source {sourceIndex + 1} is not readable.",
                        MessageType.Error);
                    return;
                }

                EventBindingComparisonOperator comparisonOperator =
                    (EventBindingComparisonOperator)clause
                        .FindPropertyRelative("comparisonOperator")
                        .enumValueIndex;

                if (!EventBindingConditionEvaluator.IsOperatorSupported(
                        metadata.ValueType,
                        comparisonOperator))
                {
                    EditorGUILayout.HelpBox(
                        $"Clause {i + 1}: Type '{metadata.ValueType.Name}' does not support C# operator '{GetComparisonOperatorLabel(comparisonOperator)}'.",
                        MessageType.Error);
                    return;
                }
            }
        }

        private void AddEventBinding()
        {
            int index = eventBindingsProperty.arraySize;
            eventBindingsProperty.arraySize++;
            SerializedProperty binding = eventBindingsProperty.GetArrayElementAtIndex(index);

            binding.FindPropertyRelative("id").stringValue = Guid.NewGuid().ToString("N");
            binding.FindPropertyRelative("name").stringValue = "Event Binding";
            binding.FindPropertyRelative("enabled").boolValue = true;
            binding.FindPropertyRelative("updateTiming").enumValueIndex = (int)BindingUpdateTiming.Update;
            binding.FindPropertyRelative("triggerOnInitialize").boolValue = false;

            SerializedProperty sources = binding.FindPropertyRelative("sources");
            sources.arraySize = 1;
            ResetSource(sources.GetArrayElementAtIndex(0));

            SerializedProperty conditions = binding.FindPropertyRelative("conditions");
            conditions.arraySize = 1;
            ResetCondition(conditions.GetArrayElementAtIndex(0));
        }

        private static void ResetSource(SerializedProperty sourceProperty)
        {
            BindingEndpointInspectorUtility.ResetEndpoint(sourceProperty.FindPropertyRelative("endpoint"));
        }

        private static void ResetCondition(SerializedProperty conditionProperty)
        {
            conditionProperty.FindPropertyRelative("name").stringValue = "Condition";
            conditionProperty.FindPropertyRelative("enabled").boolValue = true;

            SerializedProperty clauses = conditionProperty.FindPropertyRelative("clauses");
            clauses.arraySize = 1;
            ResetClause(clauses.GetArrayElementAtIndex(0));

            conditionProperty.FindPropertyRelative("actions").ClearArray();
        }

        private static void ResetClause(SerializedProperty clauseProperty)
        {
            clauseProperty.FindPropertyRelative("sourceIndex").intValue = 0;
            clauseProperty.FindPropertyRelative("logicalOperator").enumValueIndex =
                (int)EventBindingLogicalOperator.And;
            clauseProperty.FindPropertyRelative("negate").boolValue = false;
            clauseProperty.FindPropertyRelative("comparisonOperator").enumValueIndex =
                (int)EventBindingComparisonOperator.Equal;

            ResetSerializedValue(clauseProperty.FindPropertyRelative("comparisonValue"));
        }

        private static void ResetSerializedValue(SerializedProperty valueProperty)
        {
            valueProperty.FindPropertyRelative("stringValue").stringValue = string.Empty;
            valueProperty.FindPropertyRelative("boolValue").boolValue = false;
            valueProperty.FindPropertyRelative("intValue").intValue = 0;
            valueProperty.FindPropertyRelative("longValue").longValue = 0L;
            valueProperty.FindPropertyRelative("floatValue").floatValue = 0f;
            valueProperty.FindPropertyRelative("doubleValue").doubleValue = 0d;
            valueProperty.FindPropertyRelative("vector2Value").vector2Value = Vector2.zero;
            valueProperty.FindPropertyRelative("vector3Value").vector3Value = Vector3.zero;
            valueProperty.FindPropertyRelative("vector4Value").vector4Value = Vector4.zero;
            valueProperty.FindPropertyRelative("colorValue").colorValue = Color.white;
            valueProperty.FindPropertyRelative("quaternionValue").quaternionValue = Quaternion.identity;
            valueProperty.FindPropertyRelative("objectValue").objectReferenceValue = null;
            valueProperty.FindPropertyRelative("serializedValue").stringValue = "0";
        }

        private static void ResetAction(SerializedProperty actionProperty)
        {
            actionProperty.FindPropertyRelative("parameterMode").enumValueIndex =
                (int)EventBindingActionParameterMode.None;

            ClearUnityEvent(actionProperty.FindPropertyRelative("eventWithoutParameters"));
            ClearUnityEvent(actionProperty.FindPropertyRelative("oldValueEvent"));
            ClearUnityEvent(actionProperty.FindPropertyRelative("newValueEvent"));
            ClearUnityEvent(actionProperty.FindPropertyRelative("oldAndNewValuesEvent"));
        }

        private static void ClearUnityEvent(SerializedProperty eventProperty)
        {
            SerializedProperty calls = eventProperty?
                .FindPropertyRelative("m_PersistentCalls")?
                .FindPropertyRelative("m_Calls");

            if (calls != null)
            {
                calls.ClearArray();
            }
        }

        private static void RemapConditionSourceIndices(
            SerializedProperty conditionsProperty,
            int removedSourceIndex,
            int sourceCount)
        {
            int maxIndex = Mathf.Max(0, sourceCount - 1);

            for (int conditionIndex = 0; conditionIndex < conditionsProperty.arraySize; conditionIndex++)
            {
                SerializedProperty clauses = conditionsProperty
                    .GetArrayElementAtIndex(conditionIndex)
                    .FindPropertyRelative("clauses");

                for (int clauseIndex = 0; clauseIndex < clauses.arraySize; clauseIndex++)
                {
                    SerializedProperty sourceIndex = clauses
                        .GetArrayElementAtIndex(clauseIndex)
                        .FindPropertyRelative("sourceIndex");

                    if (sourceIndex.intValue > removedSourceIndex)
                    {
                        sourceIndex.intValue--;
                    }
                    else if (sourceIndex.intValue == removedSourceIndex)
                    {
                        sourceIndex.intValue = Mathf.Min(removedSourceIndex, maxIndex);
                    }

                    sourceIndex.intValue = Mathf.Clamp(sourceIndex.intValue, 0, maxIndex);
                }
            }
        }

        private static string[] BuildSourceLabels(SerializedProperty sourcesProperty)
        {
            var labels = new string[Mathf.Max(1, sourcesProperty.arraySize)];

            if (sourcesProperty.arraySize == 0)
            {
                labels[0] = "No Sources";
                return labels;
            }

            for (int i = 0; i < sourcesProperty.arraySize; i++)
            {
                SerializedProperty endpoint = sourcesProperty
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("endpoint");
                string memberPath = endpoint.FindPropertyRelative("memberPath").stringValue;
                string memberLabel = string.IsNullOrWhiteSpace(memberPath)
                    ? "Unselected"
                    : ComponentBindingPath.GetDisplayPath(memberPath);

                labels[i] = $"Source {i + 1}: {memberLabel}";
            }

            return labels;
        }

        private static Type GetSourceType(SerializedProperty sourcesProperty, int sourceIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= sourcesProperty.arraySize)
            {
                return null;
            }

            SerializedProperty endpoint = sourcesProperty
                .GetArrayElementAtIndex(sourceIndex)
                .FindPropertyRelative("endpoint");

            return BindingEditorResolver.TryGetMemberMetadata(
                    endpoint,
                    out BindingMemberMetadata metadata,
                    out _)
                ? metadata.ValueType
                : null;
        }

        private static string BuildExpressionPreview(
            SerializedProperty clausesProperty,
            IReadOnlyList<string> sourceLabels)
        {
            if (clausesProperty.arraySize == 0)
            {
                return "Expression: <empty>";
            }

            var parts = new List<string>();

            for (int i = 0; i < clausesProperty.arraySize; i++)
            {
                SerializedProperty clause = clausesProperty.GetArrayElementAtIndex(i);
                int sourceIndex = clause.FindPropertyRelative("sourceIndex").intValue;
                int comparisonIndex = clause.FindPropertyRelative("comparisonOperator").enumValueIndex;
                bool negate = clause.FindPropertyRelative("negate").boolValue;

                if (i > 0)
                {
                    int logicalIndex = clause.FindPropertyRelative("logicalOperator").enumValueIndex;
                    logicalIndex = Mathf.Clamp(logicalIndex, 0, LogicalOperatorLabels.Length - 1);
                    parts.Add(LogicalOperatorLabels[logicalIndex]);
                }

                string source = sourceIndex >= 0 && sourceIndex < sourceLabels.Count
                    ? sourceLabels[sourceIndex]
                    : $"Source {sourceIndex + 1}";
                comparisonIndex = Mathf.Clamp(comparisonIndex, 0, ComparisonOperatorLabels.Length - 1);
                string expression = $"{source} {ComparisonOperatorLabels[comparisonIndex]}";
                parts.Add(negate ? $"NOT ({expression})" : expression);
            }

            return $"Expression: {string.Join(" ", parts)}";
        }

        private static string GetComparisonOperatorLabel(
            EventBindingComparisonOperator comparisonOperator)
        {
            int index = (int)comparisonOperator;
            return index >= 0 && index < ComparisonOperatorLabels.Length
                ? ComparisonOperatorLabels[index]
                : comparisonOperator.ToString();
        }

        private static void EnsureBindingId(SerializedProperty idProperty)
        {
            if (string.IsNullOrWhiteSpace(idProperty.stringValue))
            {
                idProperty.stringValue = Guid.NewGuid().ToString("N");
            }
        }

        private static void DrawSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, BindingInspectorStyles.SectionTitleStyle);
        }
    }
}

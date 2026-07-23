using System;
using System.Collections.Generic;
using System.Reflection;
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

        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
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
                    "Observe Source members, evaluate visual conditions, and invoke UnityEvents or Task methods when values change.",
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
            SerializedProperty errorPolicyProperty = bindingProperty.FindPropertyRelative("errorPolicy");
            SerializedProperty sourceMissingPolicyProperty = bindingProperty.FindPropertyRelative("sourceMissingPolicy");
            SerializedProperty retryIntervalProperty = bindingProperty.FindPropertyRelative("missingEndpointRetryInterval");
            SerializedProperty maximumRetryIntervalProperty = bindingProperty.FindPropertyRelative("maximumMissingEndpointRetryInterval");
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

                    DrawBindingIndicator(bindingProperty, bindingIndex, enabledProperty.boolValue);
                    enabledProperty.boolValue = EditorGUILayout.Toggle(enabledProperty.boolValue, GUILayout.Width(18f));
                    nameProperty.stringValue = EditorGUILayout.TextField(nameProperty.stringValue, EditorStyles.boldLabel);

                    using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    {
                        if (GUILayout.Button("Process", EditorStyles.miniButton, GUILayout.Width(54f)))
                        {
                            serializedObject.ApplyModifiedProperties();
                            binder.ProcessEventBindingDetailed(bindingIndex, out _);
                        }

                        if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(44f)))
                        {
                            binder.InvalidateEventBinding(bindingIndex);
                        }
                    }

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
                EditorGUILayout.PropertyField(
                    errorPolicyProperty,
                    new GUIContent(
                        "Error Policy",
                        "Controls logging, runtime disabling, or exception behavior after an event binding failure."));
                EditorGUILayout.PropertyField(
                    sourceMissingPolicyProperty,
                    new GUIContent("Missing Source", "Controls behavior when a Source instance or context disappears."));
                EditorGUILayout.PropertyField(
                    retryIntervalProperty,
                    new GUIContent("Missing Retry Interval", "Initial delay between endpoint recovery attempts."));
                EditorGUILayout.PropertyField(
                    maximumRetryIntervalProperty,
                    new GUIContent("Maximum Retry Interval", "Maximum exponential backoff delay while a Source remains unavailable."));

                EditorGUILayout.Space(7f);
                DrawSectionTitle("SOURCES");
                DrawSources(sourcesProperty, conditionsProperty);

                EditorGUILayout.Space(7f);
                DrawSectionTitle("CONDITIONS");
                DrawConditions(conditionsProperty, sourcesProperty, key);

                EditorGUILayout.Space(6f);
                DrawRuntimeStatus(bindingIndex);
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

        private void DrawActions(SerializedProperty actionsProperty)
        {
            for (int i = 0; i < actionsProperty.arraySize; i++)
            {
                SerializedProperty actionProperty = actionsProperty.GetArrayElementAtIndex(i);
                SerializedProperty actionKindProperty = actionProperty.FindPropertyRelative("actionKind");
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

                    EditorGUILayout.PropertyField(actionKindProperty, new GUIContent("Action Type"));

                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(parameterModeProperty, new GUIContent("Parameters"));
                    if (EditorGUI.EndChangeCheck())
                    {
                        actionProperty.FindPropertyRelative("taskMethodSignature").stringValue = string.Empty;
                    }

                    EventBindingActionKind actionKind =
                        (EventBindingActionKind)actionKindProperty.enumValueIndex;
                    EventBindingActionParameterMode mode =
                        (EventBindingActionParameterMode)parameterModeProperty.enumValueIndex;

                    if (actionKind == EventBindingActionKind.UnityEvent)
                    {
                        SerializedProperty eventProperty = GetSelectedUnityEventProperty(actionProperty, mode);
                        EditorGUILayout.PropertyField(eventProperty, new GUIContent("Unity Event"), true);
                    }
                    else
                    {
                        DrawTaskMethodAction(actionProperty, mode);
                    }
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

        private void DrawTaskMethodAction(
            SerializedProperty actionProperty,
            EventBindingActionParameterMode parameterMode)
        {
            SerializedProperty targetProperty = actionProperty.FindPropertyRelative("taskMethodTarget");
            SerializedProperty signatureProperty = actionProperty.FindPropertyRelative("taskMethodSignature");
            SerializedProperty preventConcurrentProperty =
                actionProperty.FindPropertyRelative("preventConcurrentExecution");

            EditorGUI.BeginChangeCheck();
            DrawInstanceReference(targetProperty);
            if (EditorGUI.EndChangeCheck())
            {
                signatureProperty.stringValue = string.Empty;
            }

            DrawTaskMethodSelector(targetProperty, signatureProperty, parameterMode);
            EditorGUILayout.PropertyField(
                preventConcurrentProperty,
                new GUIContent(
                    "Prevent Concurrent",
                    "When enabled, a new invocation is skipped while the previous Task is still running."));

            if (BindingEditorResolver.TryResolveInstance(
                    targetProperty,
                    out BindingInstanceHandle handle,
                    out _) &&
                !string.IsNullOrWhiteSpace(signatureProperty.stringValue) &&
                !TaskMethodBindingUtility.TryResolveMethod(
                    handle,
                    signatureProperty.stringValue,
                    parameterMode,
                    out _,
                    out string methodError))
            {
                EditorGUILayout.HelpBox(methodError, MessageType.Warning);
            }
        }

        private void DrawInstanceReference(SerializedProperty instanceProperty)
        {
            SerializedProperty kindProperty = instanceProperty.FindPropertyRelative("kind");
            SerializedProperty objectProperty = instanceProperty.FindPropertyRelative("objectReference");
            SerializedProperty staticTypeProperty = instanceProperty.FindPropertyRelative("staticTypeName");
            SerializedProperty providerProperty = instanceProperty.FindPropertyRelative("providerReference");

            EditorGUILayout.PropertyField(kindProperty, new GUIContent("Task Target"));
            BindingInstanceKind kind = (BindingInstanceKind)kindProperty.enumValueIndex;

            switch (kind)
            {
                case BindingInstanceKind.UnityObject:
                    EditorGUILayout.PropertyField(objectProperty, new GUIContent("Instance"));
                    break;

                case BindingInstanceKind.StaticType:
                    DrawStaticTypeSelector(staticTypeProperty);
                    break;

                case BindingInstanceKind.Provider:
                    EditorGUILayout.PropertyField(providerProperty, new GUIContent("Provider"));
                    if (providerProperty.objectReferenceValue != null &&
                        !(providerProperty.objectReferenceValue is IBindingInstanceProvider))
                    {
                        EditorGUILayout.HelpBox(
                            "The selected object does not implement IBindingInstanceProvider.",
                            MessageType.Error);
                    }
                    break;
            }
        }

        private void DrawStaticTypeSelector(SerializedProperty staticTypeProperty)
        {
            Type currentType = DefaultBindingInstanceResolver.FindType(staticTypeProperty.stringValue);
            string label = currentType == null ? "Select Static Type" : currentType.FullName;

            Rect buttonRect = EditorGUILayout.GetControlRect();
            buttonRect = EditorGUI.PrefixLabel(buttonRect, new GUIContent("Static Type"));

            if (GUI.Button(buttonRect, label, BindingInspectorStyles.PathButtonStyle))
            {
                SerializedObject owner = serializedObject;
                string propertyPath = staticTypeProperty.propertyPath;
                PopupWindow.Show(
                    buttonRect,
                    new StaticTypePickerWindow(type =>
                    {
                        owner.Update();
                        SerializedProperty property = owner.FindProperty(propertyPath);
                        if (property != null)
                        {
                            property.stringValue = type.AssemblyQualifiedName;
                            owner.ApplyModifiedProperties();
                        }
                    }));
            }
        }

        private void DrawTaskMethodSelector(
            SerializedProperty targetProperty,
            SerializedProperty signatureProperty,
            EventBindingActionParameterMode parameterMode)
        {
            Rect row = EditorGUILayout.GetControlRect();
            Rect buttonRect = EditorGUI.PrefixLabel(row, new GUIContent("Task Method"));

            bool resolved = BindingEditorResolver.TryResolveInstance(
                targetProperty,
                out BindingInstanceHandle handle,
                out string resolveError);
            string label = GetTaskMethodLabel(handle, signatureProperty.stringValue, parameterMode);

            using (new EditorGUI.DisabledScope(!resolved))
            {
                if (GUI.Button(buttonRect, label, BindingInspectorStyles.PathButtonStyle))
                {
                    ShowTaskMethodMenu(buttonRect, targetProperty, signatureProperty, parameterMode);
                }
            }

            if (!resolved && !string.IsNullOrWhiteSpace(resolveError))
            {
                EditorGUILayout.HelpBox(resolveError, MessageType.Info);
            }
        }

        private void ShowTaskMethodMenu(
            Rect buttonRect,
            SerializedProperty targetProperty,
            SerializedProperty signatureProperty,
            EventBindingActionParameterMode parameterMode)
        {
            if (!BindingEditorResolver.TryResolveInstance(
                    targetProperty,
                    out BindingInstanceHandle handle,
                    out _))
            {
                return;
            }

            BindingFlags flags = handle.IsStatic
                ? BindingFlags.Public | BindingFlags.Static
                : BindingFlags.Public | BindingFlags.Instance;
            MethodInfo[] methods = handle.Type.GetMethods(flags);
            var menu = new GenericMenu();
            bool hasMethods = false;
            string propertyPath = signatureProperty.propertyPath;
            SerializedObject owner = serializedObject;

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!TaskMethodBindingUtility.IsSupportedMethod(method, parameterMode, handle.IsStatic))
                {
                    continue;
                }

                hasMethods = true;
                string signature = TaskMethodBindingUtility.CreateSignature(method);
                string displayName = TaskMethodBindingUtility.GetDisplaySignature(method);
                bool selected = string.Equals(
                    signatureProperty.stringValue,
                    signature,
                    StringComparison.Ordinal);
                menu.AddItem(new GUIContent(displayName), selected, () =>
                {
                    owner.Update();
                    SerializedProperty property = owner.FindProperty(propertyPath);
                    if (property != null)
                    {
                        property.stringValue = signature;
                        owner.ApplyModifiedProperties();
                    }
                });
            }

            if (!hasMethods)
            {
                menu.AddDisabledItem(new GUIContent("No compatible public Task methods"));
            }

            menu.DropDown(buttonRect);
        }

        private static string GetTaskMethodLabel(
            BindingInstanceHandle handle,
            string signature,
            EventBindingActionParameterMode parameterMode)
        {
            if (handle.Type == null)
            {
                return "Select Task Method";
            }

            if (TaskMethodBindingUtility.TryResolveMethod(
                    handle,
                    signature,
                    parameterMode,
                    out MethodInfo method,
                    out _))
            {
                return TaskMethodBindingUtility.GetDisplaySignature(method);
            }

            return "Select Task Method";
        }

        private void DrawBindingIndicator(
            SerializedProperty bindingProperty,
            int bindingIndex,
            bool enabled)
        {
            Color color;
            string tooltip;

            if (!enabled)
            {
                color = new Color(0.55f, 0.55f, 0.55f);
                tooltip = "Disabled";
            }
            else if (Application.isPlaying && binder.IsTaskRunning(bindingIndex))
            {
                color = new Color(0.25f, 0.7f, 1f);
                tooltip = "A Task action is running.";
            }
            else if (Application.isPlaying && binder.TryGetLastResult(bindingIndex, out BindingSyncResult result))
            {
                if (result.Status == BindingSyncStatus.Success)
                {
                    color = new Color(0.25f, 0.8f, 0.35f);
                }
                else if (result.Status == BindingSyncStatus.NoChange)
                {
                    color = new Color(0.3f, 0.65f, 0.95f);
                }
                else if (result.Status == BindingSyncStatus.Disabled)
                {
                    color = new Color(0.55f, 0.55f, 0.55f);
                }
                else
                {
                    color = new Color(0.95f, 0.3f, 0.25f);
                }

                tooltip = string.IsNullOrEmpty(result.Message)
                    ? result.Status.ToString()
                    : result.Status + ": " + result.Message;
            }
            else if (TryValidateIndicator(bindingProperty, out string validationError))
            {
                color = new Color(0.25f, 0.8f, 0.35f);
                tooltip = "Configuration is valid.";
            }
            else
            {
                color = new Color(0.95f, 0.65f, 0.2f);
                tooltip = validationError;
            }

            Color previousColor = GUI.color;
            GUI.color = color;
            GUILayout.Label(new GUIContent("●", tooltip), GUILayout.Width(14f));
            GUI.color = previousColor;
        }

        private static bool TryValidateIndicator(
            SerializedProperty bindingProperty,
            out string error)
        {
            SerializedProperty sources = bindingProperty.FindPropertyRelative("sources");
            if (sources == null || sources.arraySize == 0)
            {
                error = "At least one Source is required.";
                return false;
            }

            var sourceMetadata = new List<BindingMemberMetadata>(sources.arraySize);
            for (int i = 0; i < sources.arraySize; i++)
            {
                SerializedProperty endpoint = sources
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("endpoint");
                if (!BindingEditorResolver.TryGetMemberMetadata(
                        endpoint,
                        out BindingMemberMetadata metadata,
                        out error))
                {
                    error = $"Source {i + 1}: {error}";
                    return false;
                }

                if (!metadata.CanRead)
                {
                    error = $"Source {i + 1} is not readable.";
                    return false;
                }

                sourceMetadata.Add(metadata);
            }

            SerializedProperty conditions = bindingProperty.FindPropertyRelative("conditions");
            if (conditions == null || conditions.arraySize == 0)
            {
                error = "At least one Condition is required.";
                return false;
            }

            for (int conditionIndex = 0; conditionIndex < conditions.arraySize; conditionIndex++)
            {
                SerializedProperty condition = conditions.GetArrayElementAtIndex(conditionIndex);
                if (!condition.FindPropertyRelative("enabled").boolValue)
                {
                    continue;
                }

                SerializedProperty clauses = condition.FindPropertyRelative("clauses");
                if (clauses.arraySize == 0)
                {
                    error = $"Condition {conditionIndex + 1} has no Clauses.";
                    return false;
                }

                for (int clauseIndex = 0; clauseIndex < clauses.arraySize; clauseIndex++)
                {
                    int sourceIndex = clauses
                        .GetArrayElementAtIndex(clauseIndex)
                        .FindPropertyRelative("sourceIndex")
                        .intValue;
                    if (sourceIndex < 0 || sourceIndex >= sources.arraySize)
                    {
                        error = $"Condition {conditionIndex + 1}, Clause {clauseIndex + 1} references an invalid Source.";
                        return false;
                    }

                    EventBindingComparisonOperator comparisonOperator =
                        (EventBindingComparisonOperator)clauses
                            .GetArrayElementAtIndex(clauseIndex)
                            .FindPropertyRelative("comparisonOperator")
                            .enumValueIndex;
                    if (!EventBindingConditionEvaluator.IsOperatorSupported(
                            sourceMetadata[sourceIndex].ValueType,
                            comparisonOperator))
                    {
                        error = $"Condition {conditionIndex + 1}, Clause {clauseIndex + 1} uses an unsupported operator.";
                        return false;
                    }
                }

                SerializedProperty actions = condition.FindPropertyRelative("actions");
                for (int actionIndex = 0; actionIndex < actions.arraySize; actionIndex++)
                {
                    SerializedProperty action = actions.GetArrayElementAtIndex(actionIndex);
                    EventBindingActionKind actionKind = (EventBindingActionKind)action
                        .FindPropertyRelative("actionKind")
                        .enumValueIndex;
                    if (actionKind != EventBindingActionKind.TaskMethod)
                    {
                        continue;
                    }

                    SerializedProperty taskTarget = action.FindPropertyRelative("taskMethodTarget");
                    SerializedProperty signature = action.FindPropertyRelative("taskMethodSignature");
                    EventBindingActionParameterMode parameterMode =
                        (EventBindingActionParameterMode)action
                            .FindPropertyRelative("parameterMode")
                            .enumValueIndex;

                    if (!BindingEditorResolver.TryResolveInstance(
                            taskTarget,
                            out BindingInstanceHandle handle,
                            out error))
                    {
                        error = $"Condition {conditionIndex + 1}, Action {actionIndex + 1}: {error}";
                        return false;
                    }

                    if (!TaskMethodBindingUtility.TryResolveMethod(
                            handle,
                            signature.stringValue,
                            parameterMode,
                            out _,
                            out error))
                    {
                        error = $"Condition {conditionIndex + 1}, Action {actionIndex + 1}: {error}";
                        return false;
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        private void DrawRuntimeStatus(int bindingIndex)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (binder.IsTaskRunning(bindingIndex))
            {
                EditorGUILayout.HelpBox("One or more Task actions are running.", MessageType.Info);
            }

            if (!binder.TryGetLastResult(bindingIndex, out BindingSyncResult result))
            {
                return;
            }

            if (result.Status == BindingSyncStatus.Success || result.Status == BindingSyncStatus.NoChange)
            {
                if (!string.IsNullOrEmpty(result.Message))
                {
                    EditorGUILayout.LabelField(result.Message, EditorStyles.wordWrappedMiniLabel);
                }

                return;
            }

            EditorGUILayout.HelpBox(
                $"Runtime: {result.Status} — {result.Message}",
                MessageType.Warning);
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
            binding.FindPropertyRelative("errorPolicy").enumValueIndex =
                (int)BindingErrorPolicy.ReportOnly;
            binding.FindPropertyRelative("sourceMissingPolicy").enumValueIndex =
                (int)MissingEndpointPolicy.ReportError;

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
            actionProperty.FindPropertyRelative("actionKind").enumValueIndex =
                (int)EventBindingActionKind.UnityEvent;
            actionProperty.FindPropertyRelative("parameterMode").enumValueIndex =
                (int)EventBindingActionParameterMode.None;
            actionProperty.FindPropertyRelative("taskMethodSignature").stringValue = string.Empty;
            actionProperty.FindPropertyRelative("preventConcurrentExecution").boolValue = true;
            BindingEndpointInspectorUtility.ResetInstanceReference(
                actionProperty.FindPropertyRelative("taskMethodTarget"));

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

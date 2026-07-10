using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    [CustomEditor(typeof(ViewDataBinder))]
    public sealed class ViewDataBinderEditor : UnityEditor.Editor
    {
        private const int MemberTreeDepth = 8;

        private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> optionsFoldouts = new Dictionary<string, bool>();
        private SerializedProperty bindingsProperty;
        private ViewDataBinder binder;

        private void OnEnable()
        {
            binder = (ViewDataBinder)target;
            bindingsProperty = serializedObject.FindProperty("bindings");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTopBar();
            EditorGUILayout.Space(6f);

            for (int i = 0; i < bindingsProperty.arraySize; i++)
            {
                SerializedProperty bindingProperty = bindingsProperty.GetArrayElementAtIndex(i);
                bool remove = DrawBinding(bindingProperty, i);

                if (remove)
                {
                    bindingsProperty.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("+ Add Binding", GUILayout.Height(26f)))
            {
                AddBinding();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTopBar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("View Data Binder", BindingInspectorStyles.HeaderStyle);
                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    {
                        if (GUILayout.Button("Sync Manual", GUILayout.Width(92f)))
                        {
                            serializedObject.ApplyModifiedProperties();
                            binder.SynchronizeManualBindings();
                        }

                        if (GUILayout.Button("Sync All", GUILayout.Width(76f)))
                        {
                            serializedObject.ApplyModifiedProperties();
                            binder.SynchronizeAll();
                        }
                    }
                }

                EditorGUILayout.LabelField(
                    "Declarative member-to-member synchronization with replaceable instance, member and source backends.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private bool DrawBinding(SerializedProperty bindingProperty, int bindingIndex)
        {
            SerializedProperty idProperty = bindingProperty.FindPropertyRelative("id");
            SerializedProperty nameProperty = bindingProperty.FindPropertyRelative("name");
            SerializedProperty enabledProperty = bindingProperty.FindPropertyRelative("enabled");
            SerializedProperty sourcesProperty = bindingProperty.FindPropertyRelative("sources");
            SerializedProperty targetProperty = bindingProperty.FindPropertyRelative("target");
            SerializedProperty directionProperty = bindingProperty.FindPropertyRelative("direction");
            SerializedProperty updateTimingProperty = bindingProperty.FindPropertyRelative("updateTiming");
            SerializedProperty conflictProperty = bindingProperty.FindPropertyRelative("conflictResolution");
            SerializedProperty formatterProperty = bindingProperty.FindPropertyRelative("formatter");
            SerializedProperty converterProperty = bindingProperty.FindPropertyRelative("converter");
            SerializedProperty nullHandlingProperty = bindingProperty.FindPropertyRelative("nullHandling");
            SerializedProperty fallbackProperty = bindingProperty.FindPropertyRelative("fallback");

            EnsureBindingId(idProperty);
            string foldoutKey = string.IsNullOrEmpty(idProperty.stringValue)
                ? bindingIndex.ToString()
                : idProperty.stringValue;

            if (!foldouts.TryGetValue(foldoutKey, out bool expanded))
            {
                expanded = true;
                foldouts[foldoutKey] = true;
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
                    foldouts[foldoutKey] = expanded;

                    enabledProperty.boolValue = EditorGUILayout.Toggle(enabledProperty.boolValue, GUILayout.Width(18f));
                    nameProperty.stringValue = EditorGUILayout.TextField(nameProperty.stringValue, EditorStyles.boldLabel);

                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24f)))
                    {
                        remove = true;
                    }
                }

                if (!expanded)
                {
                    DrawCollapsedSummary(directionProperty, updateTimingProperty);
                    return remove;
                }

                EditorGUILayout.Space(3f);
                DrawSettings(directionProperty, updateTimingProperty, conflictProperty);
                EditorGUILayout.Space(6f);

                BindingSyncDirection direction = (BindingSyncDirection)directionProperty.enumValueIndex;

                DrawExtensions(
                    bindingProperty,
                    bindingIndex,
                    BindingInspectorExtensionPlacement.BeforeSources);

                DrawSectionTitle("SOURCES");
                DrawSources(sourcesProperty, direction, formatterProperty);

                EditorGUILayout.Space(7f);
                DrawOptions(
                    foldoutKey,
                    formatterProperty,
                    converterProperty,
                    nullHandlingProperty,
                    fallbackProperty,
                    direction,
                    sourcesProperty);

                DrawExtensions(
                    bindingProperty,
                    bindingIndex,
                    BindingInspectorExtensionPlacement.AfterSources);

                EditorGUILayout.Space(7f);
                DrawExtensions(
                    bindingProperty,
                    bindingIndex,
                    BindingInspectorExtensionPlacement.BeforeTarget);

                DrawSectionTitle("TARGET");
                Type compatibleTargetType = GetCompatibleTargetType(
                    GetEffectiveSourceType(sourcesProperty, formatterProperty),
                    converterProperty);
                DrawEndpoint(
                    targetProperty,
                    GetTargetReadRequirement(direction),
                    GetTargetWriteRequirement(direction),
                    "Select Target Member",
                    compatibleTargetType);
                DrawTargetValuePreview(bindingIndex, direction, targetProperty);

                DrawExtensions(
                    bindingProperty,
                    bindingIndex,
                    BindingInspectorExtensionPlacement.AfterTarget);

                EditorGUILayout.Space(7f);
                DrawValidation(
                    sourcesProperty,
                    targetProperty,
                    formatterProperty,
                    converterProperty,
                    nullHandlingProperty,
                    fallbackProperty,
                    direction);
                DrawRuntimeStatus(bindingIndex);
                DrawExtensions(
                    bindingProperty,
                    bindingIndex,
                    BindingInspectorExtensionPlacement.AfterValidation);
            }

            return remove;
        }

        private static void DrawCollapsedSummary(
            SerializedProperty directionProperty,
            SerializedProperty updateTimingProperty)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(34f);
                GUILayout.Label(
                    $"{directionProperty.enumDisplayNames[directionProperty.enumValueIndex]}  •  {updateTimingProperty.enumDisplayNames[updateTimingProperty.enumValueIndex]}",
                    EditorStyles.miniLabel);
            }
        }

        private static void DrawSettings(
            SerializedProperty directionProperty,
            SerializedProperty updateTimingProperty,
            SerializedProperty conflictProperty)
        {
            EditorGUILayout.PropertyField(directionProperty, new GUIContent("Direction"));
            EditorGUILayout.PropertyField(updateTimingProperty, new GUIContent("Polling"));

            if ((BindingSyncDirection)directionProperty.enumValueIndex == BindingSyncDirection.TwoWay)
            {
                EditorGUILayout.PropertyField(conflictProperty, new GUIContent("Conflict Resolution"));
            }
        }

        private void DrawOptions(
            string bindingKey,
            SerializedProperty formatterProperty,
            SerializedProperty converterProperty,
            SerializedProperty nullHandlingProperty,
            SerializedProperty fallbackProperty,
            BindingSyncDirection direction,
            SerializedProperty sourcesProperty)
        {
            string optionsKey = $"{bindingKey}:options";
            if (!optionsFoldouts.TryGetValue(optionsKey, out bool expanded))
            {
                expanded = false;
                optionsFoldouts[optionsKey] = false;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Rect headerRect = EditorGUILayout.GetControlRect(
                    false,
                    EditorGUIUtility.singleLineHeight);
                expanded = EditorGUI.Foldout(
                    headerRect,
                    expanded,
                    "Options",
                    true);
                optionsFoldouts[optionsKey] = expanded;

                if (!expanded)
                {
                    return;
                }

                EditorGUILayout.Space(3f);
                DrawSectionTitle("FORMATTER");
                DrawFormatter(formatterProperty, direction, sourcesProperty);

                EditorGUILayout.Space(7f);
                DrawSectionTitle("CONVERTER");
                DrawConverter(converterProperty, direction, sourcesProperty, formatterProperty);

                EditorGUILayout.Space(7f);
                DrawSectionTitle("VALUE HANDLING");
                Type effectiveSourceType = GetEffectiveSourceType(sourcesProperty, formatterProperty);
                DrawValueHandling(nullHandlingProperty, fallbackProperty, effectiveSourceType);
            }
        }

        private void DrawSources(
            SerializedProperty sourcesProperty,
            BindingSyncDirection direction,
            SerializedProperty formatterProperty)
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
                                return;
                            }
                        }
                    }

                    DrawEndpoint(
                        endpointProperty,
                        GetSourceReadRequirement(direction),
                        GetSourceWriteRequirement(direction),
                        "Select Source Member");
                }
            }

            if (GUILayout.Button("+ Add Source", EditorStyles.miniButton))
            {
                int index = sourcesProperty.arraySize;
                sourcesProperty.arraySize++;
                ResetSource(sourcesProperty.GetArrayElementAtIndex(index));
            }

            bool formatterEnabled = formatterProperty
                .FindPropertyRelative("enabled")
                .boolValue;

            if (sourcesProperty.arraySize > 1 &&
                !formatterEnabled &&
                BindingBackendRegistry.SourceBackend is SingleSourceBindingSourceBackend)
            {
                EditorGUILayout.HelpBox(
                    "Multiple Sources require a Formatter when using SingleSourceBindingSourceBackend.",
                    MessageType.Warning);
            }
        }

        private static void DrawFormatter(
            SerializedProperty formatterProperty,
            BindingSyncDirection direction,
            SerializedProperty sourcesProperty)
        {
            SerializedProperty enabledProperty = formatterProperty.FindPropertyRelative("enabled");
            SerializedProperty formatterIdProperty = formatterProperty.FindPropertyRelative("formatterId");
            SerializedProperty formatStringProperty = formatterProperty.FindPropertyRelative("formatString");
            SerializedProperty cultureNameProperty = formatterProperty.FindPropertyRelative("cultureName");

            EditorGUILayout.PropertyField(enabledProperty, new GUIContent("Enabled"));
            if (!enabledProperty.boolValue)
            {
                EditorGUILayout.LabelField(
                    "Disabled: exactly one Source is synchronized directly with the Target.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            IReadOnlyList<IBindingFormatter> formatters = BindingFormatterRegistry.Formatters;
            if (formatters.Count == 0)
            {
                EditorGUILayout.HelpBox("No binding formatters are registered.", MessageType.Error);
                return;
            }

            int selectedIndex = 0;
            var displayNames = new string[formatters.Count];
            for (int i = 0; i < formatters.Count; i++)
            {
                displayNames[i] = formatters[i].DisplayName;
                if (formatters[i].Id == formatterIdProperty.stringValue)
                {
                    selectedIndex = i;
                }
            }

            int newIndex = EditorGUILayout.Popup("Formatter", selectedIndex, displayNames);
            formatterIdProperty.stringValue = formatters[newIndex].Id;

            EditorGUILayout.PropertyField(formatStringProperty, new GUIContent("Format String"));
            EditorGUILayout.PropertyField(
                cultureNameProperty,
                new GUIContent("Culture Name", "Leave empty to use the current culture. Example: en-US or pt-BR."));

            if (direction != BindingSyncDirection.SourceToTarget)
            {
                EditorGUILayout.HelpBox(
                    "Formatter bindings support Source -> Target only because the formatted output cannot be reversed into the original Sources.",
                    MessageType.Error);
            }

            if (sourcesProperty.arraySize > 0)
            {
                var sourceMap = new string[sourcesProperty.arraySize];
                for (int i = 0; i < sourceMap.Length; i++)
                {
                    sourceMap[i] = $"{{{i}}} = Source {i + 1}";
                }

                EditorGUILayout.LabelField(
                    string.Join("   •   ", sourceMap),
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void DrawConverter(
            SerializedProperty converterProperty,
            BindingSyncDirection direction,
            SerializedProperty sourcesProperty,
            SerializedProperty formatterProperty)
        {
            EditorGUILayout.PropertyField(
                converterProperty,
                new GUIContent(
                    "Converter",
                    "Reusable ScriptableObject that converts the Source/Formatter output into the Target type."));

            BindingConverter converter = converterProperty.objectReferenceValue as BindingConverter;
            if (converter == null)
            {
                EditorGUILayout.LabelField(
                    "No converter: Source output and Target must have exactly the same Type.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            Type sourceOutputType = GetEffectiveSourceType(sourcesProperty, formatterProperty);
            string sourceTypeName = sourceOutputType != null
                ? GetFriendlyTypeName(sourceOutputType)
                : "Unresolved Source";
            string targetTypeName = GetFriendlyTypeName(converter.TargetType);
            string reverseLabel = converter.SupportsReverseConversion
                ? "Reverse: Supported"
                : "Reverse: Not Supported";

            EditorGUILayout.LabelField(
                $"{sourceTypeName} -> {converter.name} -> {targetTypeName}  •  {reverseLabel}",
                EditorStyles.wordWrappedMiniLabel);

            if (sourceOutputType != null && !converter.CanConvert(sourceOutputType, converter.TargetType))
            {
                EditorGUILayout.HelpBox(
                    $"Converter '{converter.name}' does not accept {GetFriendlyTypeName(sourceOutputType)} as Source input.",
                    MessageType.Error);
            }

            if (direction != BindingSyncDirection.SourceToTarget && !converter.SupportsReverseConversion)
            {
                EditorGUILayout.HelpBox(
                    "Target -> Source and Two-Way bindings require a Converter that supports reverse conversion.",
                    MessageType.Error);
            }
        }

        private static void DrawValueHandling(
            SerializedProperty nullHandlingProperty,
            SerializedProperty fallbackProperty,
            Type valueType)
        {
            EditorGUILayout.PropertyField(nullHandlingProperty, new GUIContent("Null Handling"));

            BindingNullHandlingMode nullHandling =
                (BindingNullHandlingMode)nullHandlingProperty.enumValueIndex;

            SerializedProperty fallbackEnabledProperty = fallbackProperty.FindPropertyRelative("enabled");
            EditorGUILayout.PropertyField(fallbackEnabledProperty, new GUIContent("Fallback Enabled"));

            if (nullHandling == BindingNullHandlingMode.UseFallback && !fallbackEnabledProperty.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Use Fallback requires Fallback Enabled.",
                    MessageType.Error);
            }

            if (!fallbackEnabledProperty.boolValue)
            {
                return;
            }

            EditorGUILayout.PropertyField(
                fallbackProperty.FindPropertyRelative("useOnReadFailure"),
                new GUIContent("On Read Failure"));
            EditorGUILayout.PropertyField(
                fallbackProperty.FindPropertyRelative("useOnFormatterFailure"),
                new GUIContent("On Formatter Failure"));
            EditorGUILayout.PropertyField(
                fallbackProperty.FindPropertyRelative("useOnConverterFailure"),
                new GUIContent("On Converter Failure"));

            SerializedProperty valueProperty = fallbackProperty.FindPropertyRelative("value");
            DrawFallbackValue(valueProperty, valueType);
        }

        private static void DrawFallbackValue(SerializedProperty valueProperty, Type valueType)
        {
            BindingSerializedValueDrawer.Draw(valueProperty, valueType, "Fallback Value");
        }

        private void DrawEndpoint(
            SerializedProperty endpointProperty,
            bool requireReadable,
            bool requireWritable,
            string emptyLabel,
            Type compatibleType = null)
        {
            BindingEndpointInspectorUtility.DrawEndpoint(
                serializedObject,
                endpointProperty,
                requireReadable,
                requireWritable,
                emptyLabel,
                compatibleType);
        }

        private void DrawInstanceReference(SerializedProperty instanceProperty)
        {
            SerializedProperty kindProperty = instanceProperty.FindPropertyRelative("kind");
            SerializedProperty objectProperty = instanceProperty.FindPropertyRelative("objectReference");
            SerializedProperty staticTypeProperty = instanceProperty.FindPropertyRelative("staticTypeName");
            SerializedProperty providerProperty = instanceProperty.FindPropertyRelative("providerReference");

            EditorGUILayout.PropertyField(kindProperty, new GUIContent("Instance Kind"));
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
            string label = currentType == null
                ? "Select Static Type"
                : currentType.FullName;

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

        private void DrawMemberSelector(
            SerializedProperty instanceProperty,
            SerializedProperty memberPathProperty,
            bool requireReadable,
            bool requireWritable,
            string emptyLabel,
            Type compatibleType)
        {
            string buttonLabel = string.IsNullOrWhiteSpace(memberPathProperty.stringValue)
                ? emptyLabel
                : ComponentBindingPath.GetDisplayPath(memberPathProperty.stringValue);

            Rect row = EditorGUILayout.GetControlRect();
            Rect buttonRect = EditorGUI.PrefixLabel(row, new GUIContent("Member"));

            bool instanceResolved = BindingEditorResolver.TryResolveInstance(
                instanceProperty,
                out BindingInstanceHandle handle,
                out string instanceError);

            using (new EditorGUI.DisabledScope(!instanceResolved))
            {
                if (GUI.Button(buttonRect, buttonLabel, BindingInspectorStyles.PathButtonStyle))
                {
                    IReadOnlyList<BindingMemberDescriptor> members =
                        BindingBackendRegistry.MemberBackend.GetMembers(handle, MemberTreeDepth);

                    SerializedObject owner = serializedObject;
                    string propertyPath = memberPathProperty.propertyPath;

                    Func<string, int, IReadOnlyList<BindingMemberDescriptor>> searchProvider = null;
                    if (BindingBackendRegistry.MemberBackend is IBindingMemberSearchBackend searchBackend)
                    {
                        searchProvider = (query, maxResults) => searchBackend.SearchMembers(
                            handle,
                            MemberTreeDepth,
                            query,
                            maxResults);
                    }

                    PopupWindow.Show(
                        buttonRect,
                        new BindingMemberPickerWindow(
                            members,
                            requireReadable,
                            requireWritable,
                            descriptor =>
                            {
                                owner.Update();
                                SerializedProperty property = owner.FindProperty(propertyPath);
                                if (property != null)
                                {
                                    property.stringValue = descriptor.Path;
                                    owner.ApplyModifiedProperties();
                                }
                            },
                            compatibleType,
                            searchProvider));
                }
            }

            if (!instanceResolved && HasAnyInstanceConfiguration(instanceProperty))
            {
                EditorGUILayout.HelpBox(instanceError, MessageType.Error);
            }
            else if (instanceResolved && !string.IsNullOrWhiteSpace(memberPathProperty.stringValue))
            {
                DrawSelectedMemberType(memberPathProperty);
            }
        }

        private void DrawSelectedMemberType(SerializedProperty memberPathProperty)
        {
            SerializedProperty endpointProperty = memberPathProperty.Copy();
            endpointProperty = endpointProperty.serializedObject.FindProperty(
                memberPathProperty.propertyPath.Substring(
                    0,
                    memberPathProperty.propertyPath.Length - ".memberPath".Length));

            if (BindingEditorResolver.TryGetMemberMetadata(
                    endpointProperty,
                    out BindingMemberMetadata metadata,
                    out string error))
            {
                string access = metadata.CanRead && metadata.CanWrite
                    ? "Read / Write"
                    : metadata.CanRead
                        ? "Read Only"
                        : metadata.CanWrite
                            ? "Write Only"
                            : "Unavailable";

                EditorGUILayout.LabelField(
                    "",
                    $"{GetFriendlyTypeName(metadata.ValueType)}  •  {access}",
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            }
        }

        private static Type GetEndpointType(SerializedProperty endpointProperty)
        {
            return BindingEditorResolver.TryGetMemberMetadata(
                    endpointProperty,
                    out BindingMemberMetadata metadata,
                    out _)
                ? metadata.ValueType
                : null;
        }

        private static Type GetEffectiveSourceType(
            SerializedProperty sourcesProperty,
            SerializedProperty formatterProperty)
        {
            if (sourcesProperty == null || sourcesProperty.arraySize == 0)
            {
                return null;
            }

            bool formatterEnabled = formatterProperty != null &&
                                    formatterProperty.FindPropertyRelative("enabled").boolValue;

            if (!formatterEnabled)
            {
                if (sourcesProperty.arraySize != 1)
                {
                    return null;
                }

                SerializedProperty sourceEndpoint = sourcesProperty
                    .GetArrayElementAtIndex(0)
                    .FindPropertyRelative("endpoint");

                return BindingEditorResolver.TryGetMemberMetadata(
                        sourceEndpoint,
                        out BindingMemberMetadata metadata,
                        out _)
                    ? metadata.ValueType
                    : null;
            }

            if (!TryGetSourceMetadataList(
                    sourcesProperty,
                    out List<BindingMemberMetadata> sourceMetadata,
                    out _))
            {
                return null;
            }

            string formatterId = formatterProperty
                .FindPropertyRelative("formatterId")
                .stringValue;

            if (!BindingFormatterRegistry.TryGet(formatterId, out IBindingFormatter formatter))
            {
                return null;
            }

            return formatter.TryGetOutputType(sourceMetadata, out Type outputType, out _)
                ? outputType
                : null;
        }

        private static Type GetCompatibleTargetType(
            Type sourceOutputType,
            SerializedProperty converterProperty)
        {
            BindingConverter converter = converterProperty != null
                ? converterProperty.objectReferenceValue as BindingConverter
                : null;

            return converter != null ? converter.TargetType : sourceOutputType;
        }

        private static bool TryGetSourceMetadataList(
            SerializedProperty sourcesProperty,
            out List<BindingMemberMetadata> metadata,
            out string error)
        {
            metadata = new List<BindingMemberMetadata>();

            if (sourcesProperty == null || sourcesProperty.arraySize == 0)
            {
                error = "At least one Source is required.";
                return false;
            }

            for (int i = 0; i < sourcesProperty.arraySize; i++)
            {
                SerializedProperty endpoint = sourcesProperty
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("endpoint");

                if (!BindingEditorResolver.TryGetMemberMetadata(
                        endpoint,
                        out BindingMemberMetadata sourceMetadata,
                        out string sourceError))
                {
                    error = $"Source {i + 1}: {sourceError}";
                    return false;
                }

                metadata.Add(sourceMetadata);
            }

            error = string.Empty;
            return true;
        }

        private void DrawTargetValuePreview(
            int bindingIndex,
            BindingSyncDirection direction,
            SerializedProperty targetProperty)
        {
            if (direction == BindingSyncDirection.TargetToSource ||
                bindingIndex < 0 ||
                bindingIndex >= binder.Bindings.Count ||
                !BindingEditorResolver.TryGetMemberMetadata(targetProperty, out _, out _))
            {
                return;
            }

            if (!binder.TryEvaluateSourceValue(
                    bindingIndex,
                    out object value,
                    out BindingSyncResult evaluationResult))
            {
                if (evaluationResult.Status != BindingSyncStatus.NoChange)
                {
                    EditorGUILayout.LabelField(
                        "Value Preview",
                        $"Unavailable: {evaluationResult.Message}",
                        EditorStyles.wordWrappedMiniLabel);
                }

                return;
            }

            string preview;
            if (value == null)
            {
                preview = "null";
            }
            else
            {
                try
                {
                    preview = value.ToString();
                }
                catch (Exception exception)
                {
                    preview = $"ToString failed: {exception.Message}";
                }
            }

            if (preview != null)
            {
                EditorGUILayout.LabelField("Value Preview", preview, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawValidation(
            SerializedProperty sourcesProperty,
            SerializedProperty targetProperty,
            SerializedProperty formatterProperty,
            SerializedProperty converterProperty,
            SerializedProperty nullHandlingProperty,
            SerializedProperty fallbackProperty,
            BindingSyncDirection direction)
        {
            bool formatterEnabled = formatterProperty
                .FindPropertyRelative("enabled")
                .boolValue;

            if (!formatterEnabled &&
                sourcesProperty.arraySize != 1 &&
                BindingBackendRegistry.SourceBackend is SingleSourceBindingSourceBackend)
            {
                EditorGUILayout.HelpBox(
                    "Without a Formatter, SingleSourceBindingSourceBackend requires exactly one Source.",
                    MessageType.Error);
                return;
            }

            if (!formatterEnabled && sourcesProperty.arraySize != 1)
            {
                EditorGUILayout.HelpBox(
                    "A custom Source backend is active. Multi-source output validation is deferred to that backend at runtime.",
                    MessageType.Info);
                return;
            }

            if (formatterEnabled && direction != BindingSyncDirection.SourceToTarget)
            {
                EditorGUILayout.HelpBox(
                    "Formatter bindings support Source -> Target only.",
                    MessageType.Error);
                return;
            }

            if (!TryGetSourceMetadataList(
                    sourcesProperty,
                    out List<BindingMemberMetadata> sourceMetadata,
                    out _))
            {
                return;
            }

            Type sourceOutputType;
            if (formatterEnabled)
            {
                for (int i = 0; i < sourceMetadata.Count; i++)
                {
                    if (!sourceMetadata[i].CanRead)
                    {
                        EditorGUILayout.HelpBox(
                            $"Source {i + 1} must be readable for Formatter bindings.",
                            MessageType.Error);
                        return;
                    }
                }

                string formatterId = formatterProperty
                    .FindPropertyRelative("formatterId")
                    .stringValue;

                if (!BindingFormatterRegistry.TryGet(formatterId, out IBindingFormatter formatter))
                {
                    EditorGUILayout.HelpBox(
                        $"Formatter '{formatterId}' is not registered.",
                        MessageType.Error);
                    return;
                }

                if (!formatter.TryGetOutputType(sourceMetadata, out sourceOutputType, out string formatterError))
                {
                    EditorGUILayout.HelpBox(formatterError, MessageType.Error);
                    return;
                }
            }
            else
            {
                sourceOutputType = sourceMetadata[0].ValueType;
            }

            if (!BindingEditorResolver.TryGetMemberMetadata(
                    targetProperty,
                    out BindingMemberMetadata targetMetadata,
                    out _))
            {
                return;
            }

            BindingConverter converter = converterProperty.objectReferenceValue as BindingConverter;
            if (converter == null)
            {
                if (sourceOutputType != targetMetadata.ValueType)
                {
                    EditorGUILayout.HelpBox(
                        $"Type mismatch. Source output: {GetFriendlyTypeName(sourceOutputType)} | Target: {GetFriendlyTypeName(targetMetadata.ValueType)}. Assign a compatible Converter to bridge these types.",
                        MessageType.Error);
                    return;
                }
            }
            else
            {
                if (!converter.CanConvert(sourceOutputType, targetMetadata.ValueType))
                {
                    EditorGUILayout.HelpBox(
                        $"Converter '{converter.name}' cannot convert {GetFriendlyTypeName(sourceOutputType)} to {GetFriendlyTypeName(targetMetadata.ValueType)}.",
                        MessageType.Error);
                    return;
                }

                if (direction != BindingSyncDirection.SourceToTarget &&
                    !converter.CanConvertBack(targetMetadata.ValueType, sourceOutputType))
                {
                    EditorGUILayout.HelpBox(
                        $"Direction '{direction}' requires reverse conversion, but Converter '{converter.name}' cannot convert {GetFriendlyTypeName(targetMetadata.ValueType)} back to {GetFriendlyTypeName(sourceOutputType)}.",
                        MessageType.Error);
                    return;
                }
            }

            if (!formatterEnabled)
            {
                string accessError = ValidateAccess(direction, sourceMetadata[0], targetMetadata);
                if (!string.IsNullOrEmpty(accessError))
                {
                    EditorGUILayout.HelpBox(accessError, MessageType.Error);
                    return;
                }
            }
            else if (!targetMetadata.CanWrite)
            {
                EditorGUILayout.HelpBox(
                    "Formatter bindings require a writable Target member.",
                    MessageType.Error);
                return;
            }

            BindingNullHandlingMode nullHandling =
                (BindingNullHandlingMode)nullHandlingProperty.enumValueIndex;
            bool fallbackEnabled = fallbackProperty
                .FindPropertyRelative("enabled")
                .boolValue;

            if (nullHandling == BindingNullHandlingMode.UseFallback && !fallbackEnabled)
            {
                EditorGUILayout.HelpBox(
                    "Null Handling is set to Use Fallback, but Fallback is disabled.",
                    MessageType.Error);
                return;
            }

            string suffix;
            if (converter != null)
            {
                suffix = $"Pipeline: {GetFriendlyTypeName(sourceOutputType)} -> {converter.name} -> {GetFriendlyTypeName(targetMetadata.ValueType)}";
            }
            else if (formatterEnabled)
            {
                suffix = $"Formatter output: {GetFriendlyTypeName(sourceOutputType)}";
            }
            else
            {
                suffix = $"Compatible: {GetFriendlyTypeName(sourceOutputType)}";
            }

            EditorGUILayout.HelpBox(suffix, MessageType.Info);
        }

        private void DrawRuntimeStatus(int bindingIndex)
        {
            if (!Application.isPlaying || !binder.TryGetLastResult(bindingIndex, out BindingSyncResult result))
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

        private void DrawExtensions(
            SerializedProperty bindingProperty,
            int bindingIndex,
            BindingInspectorExtensionPlacement placement)
        {
            IReadOnlyList<IViewDataBindingInspectorExtension> extensions =
                BindingInspectorExtensionRegistry.RegisteredExtensions;

            if (extensions.Count == 0)
            {
                return;
            }

            BindingInspectorContext context = null;
            bool drewAny = false;

            for (int i = 0; i < extensions.Count; i++)
            {
                IViewDataBindingInspectorExtension extension = extensions[i];
                if (extension.Placement != placement)
                {
                    continue;
                }

                if (!drewAny)
                {
                    EditorGUILayout.Space(6f);
                    drewAny = true;
                }

                if (context == null)
                {
                    context = new BindingInspectorContext(
                        binder,
                        serializedObject,
                        bindingProperty,
                        bindingIndex);
                }

                extension.Draw(context);
            }
        }

        private void AddBinding()
        {
            int index = bindingsProperty.arraySize;
            bindingsProperty.arraySize++;
            SerializedProperty bindingProperty = bindingsProperty.GetArrayElementAtIndex(index);

            bindingProperty.FindPropertyRelative("id").stringValue = Guid.NewGuid().ToString("N");
            bindingProperty.FindPropertyRelative("name").stringValue = $"Binding {index + 1}";
            bindingProperty.FindPropertyRelative("enabled").boolValue = true;
            bindingProperty.FindPropertyRelative("direction").enumValueIndex = (int)BindingSyncDirection.SourceToTarget;
            bindingProperty.FindPropertyRelative("updateTiming").enumValueIndex = (int)BindingUpdateTiming.Update;
            bindingProperty.FindPropertyRelative("conflictResolution").enumValueIndex = (int)BindingConflictResolution.SourceWins;
            bindingProperty.FindPropertyRelative("nullHandling").enumValueIndex = (int)BindingNullHandlingMode.PassThrough;

            SerializedProperty formatterProperty = bindingProperty.FindPropertyRelative("formatter");
            formatterProperty.FindPropertyRelative("enabled").boolValue = false;
            formatterProperty.FindPropertyRelative("formatterId").stringValue = CompositeStringBindingFormatter.FormatterId;
            formatterProperty.FindPropertyRelative("formatString").stringValue = "{0}";
            formatterProperty.FindPropertyRelative("cultureName").stringValue = string.Empty;
            bindingProperty.FindPropertyRelative("converter").objectReferenceValue = null;

            SerializedProperty fallbackProperty = bindingProperty.FindPropertyRelative("fallback");
            fallbackProperty.FindPropertyRelative("enabled").boolValue = false;
            fallbackProperty.FindPropertyRelative("useOnReadFailure").boolValue = false;
            fallbackProperty.FindPropertyRelative("useOnFormatterFailure").boolValue = false;
            fallbackProperty.FindPropertyRelative("useOnConverterFailure").boolValue = false;

            SerializedProperty sourcesProperty = bindingProperty.FindPropertyRelative("sources");
            sourcesProperty.arraySize = 1;
            ResetSource(sourcesProperty.GetArrayElementAtIndex(0));
            ResetEndpoint(bindingProperty.FindPropertyRelative("target"));
        }

        private static void ResetSource(SerializedProperty sourceProperty)
        {
            ResetEndpoint(sourceProperty.FindPropertyRelative("endpoint"));
        }

        private static void ResetEndpoint(SerializedProperty endpointProperty)
        {
            SerializedProperty instanceProperty = endpointProperty.FindPropertyRelative("instance");
            instanceProperty.FindPropertyRelative("kind").enumValueIndex = (int)BindingInstanceKind.UnityObject;
            instanceProperty.FindPropertyRelative("objectReference").objectReferenceValue = null;
            instanceProperty.FindPropertyRelative("staticTypeName").stringValue = string.Empty;
            instanceProperty.FindPropertyRelative("providerReference").objectReferenceValue = null;
            endpointProperty.FindPropertyRelative("memberPath").stringValue = string.Empty;
        }

        private static void EnsureBindingId(SerializedProperty idProperty)
        {
            if (string.IsNullOrWhiteSpace(idProperty.stringValue))
            {
                idProperty.stringValue = Guid.NewGuid().ToString("N");
            }
        }

        private static bool HasAnyInstanceConfiguration(SerializedProperty instanceProperty)
        {
            BindingInstanceKind kind = (BindingInstanceKind)instanceProperty
                .FindPropertyRelative("kind")
                .enumValueIndex;

            switch (kind)
            {
                case BindingInstanceKind.UnityObject:
                    return instanceProperty.FindPropertyRelative("objectReference").objectReferenceValue != null;

                case BindingInstanceKind.StaticType:
                    return !string.IsNullOrWhiteSpace(
                        instanceProperty.FindPropertyRelative("staticTypeName").stringValue);

                case BindingInstanceKind.Provider:
                    return instanceProperty.FindPropertyRelative("providerReference").objectReferenceValue != null;

                default:
                    return false;
            }
        }

        private static string ValidateAccess(
            BindingSyncDirection direction,
            BindingMemberMetadata source,
            BindingMemberMetadata target)
        {
            switch (direction)
            {
                case BindingSyncDirection.SourceToTarget:
                    if (!source.CanRead)
                    {
                        return "Source -> Target requires a readable Source member.";
                    }

                    if (!target.CanWrite)
                    {
                        return "Source -> Target requires a writable Target member.";
                    }

                    break;

                case BindingSyncDirection.TargetToSource:
                    if (!target.CanRead)
                    {
                        return "Target -> Source requires a readable Target member.";
                    }

                    if (!source.CanWrite)
                    {
                        return "Target -> Source requires a writable Source member.";
                    }

                    break;

                case BindingSyncDirection.TwoWay:
                    if (!source.CanRead || !source.CanWrite)
                    {
                        return "Two-way synchronization requires a readable and writable Source member.";
                    }

                    if (!target.CanRead || !target.CanWrite)
                    {
                        return "Two-way synchronization requires a readable and writable Target member.";
                    }

                    break;
            }

            return string.Empty;
        }

        private static bool GetSourceReadRequirement(BindingSyncDirection direction)
        {
            return direction == BindingSyncDirection.SourceToTarget || direction == BindingSyncDirection.TwoWay;
        }

        private static bool GetSourceWriteRequirement(BindingSyncDirection direction)
        {
            return direction == BindingSyncDirection.TargetToSource || direction == BindingSyncDirection.TwoWay;
        }

        private static bool GetTargetReadRequirement(BindingSyncDirection direction)
        {
            return direction == BindingSyncDirection.TargetToSource || direction == BindingSyncDirection.TwoWay;
        }

        private static bool GetTargetWriteRequirement(BindingSyncDirection direction)
        {
            return direction == BindingSyncDirection.SourceToTarget || direction == BindingSyncDirection.TwoWay;
        }

        private static void DrawSectionTitle(string title)
        {
            EditorGUILayout.LabelField(title, BindingInspectorStyles.SectionTitleStyle);
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == null)
            {
                return "Unknown";
            }

            if (!type.IsGenericType)
            {
                return type.Name;
            }

            string name = type.Name;
            int tickIndex = name.IndexOf('`');
            if (tickIndex >= 0)
            {
                name = name.Substring(0, tickIndex);
            }

            Type[] arguments = type.GetGenericArguments();
            var argumentNames = new string[arguments.Length];
            for (int i = 0; i < arguments.Length; i++)
            {
                argumentNames[i] = GetFriendlyTypeName(arguments[i]);
            }

            return $"{name}<{string.Join(", ", argumentNames)}>";
        }
    }
}

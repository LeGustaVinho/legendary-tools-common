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
        private readonly Dictionary<string, bool> previewFoldouts = new Dictionary<string, bool>();
        private readonly Dictionary<string, BindingPreview> previewCache =
            new Dictionary<string, BindingPreview>();
        private SerializedProperty bindingsProperty;
        private SerializedProperty profilesProperty;
        private ViewDataBinder binder;

        private void OnEnable()
        {
            binder = (ViewDataBinder)target;
            bindingsProperty = serializedObject.FindProperty("bindings");
            profilesProperty = serializedObject.FindProperty("profiles");
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
            DrawProfiles();
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

        private void DrawProfiles()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Binding Profiles", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Create from Local", GUILayout.Width(112f)))
                    {
                        CreateProfileFromLocalBindings();
                    }
                }

                EditorGUILayout.LabelField(
                    "Profile endpoints can use Context instances named $Source and $Target. Named overrides fall back to the nearest Binding Data Context.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.PropertyField(profilesProperty, true);

                if (!Application.isPlaying)
                {
                    return;
                }

                for (int profileIndex = 0; profileIndex < binder.Profiles.Count; profileIndex++)
                {
                    ViewDataBindingProfileReference profileReference = binder.Profiles[profileIndex];
                    if (profileReference == null || profileReference.Profile == null)
                    {
                        continue;
                    }

                    IReadOnlyList<ViewDataBinding> profileBindings = profileReference.Profile.Bindings;
                    for (int bindingIndex = 0; bindingIndex < profileBindings.Count; bindingIndex++)
                    {
                        ViewDataBinding binding = profileBindings[bindingIndex];
                        if (binding == null)
                        {
                            continue;
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(12f);
                            GUILayout.Label(binding.Name, EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();
                            if (binder.TryGetProfileLastResult(
                                    profileIndex,
                                    bindingIndex,
                                    out BindingSyncResult result))
                            {
                                GUILayout.Label(result.Status.ToString(), EditorStyles.miniLabel);
                            }

                            if (GUILayout.Button("Sync", EditorStyles.miniButton, GUILayout.Width(42f)))
                            {
                                binder.SynchronizeProfileBinding(profileIndex, bindingIndex);
                            }

                            if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(44f)))
                            {
                                binder.InvalidateProfileBinding(profileIndex, bindingIndex);
                            }
                        }
                    }
                }
            }
        }

        private void CreateProfileFromLocalBindings()
        {
            serializedObject.ApplyModifiedProperties();
            if (!BindingProfileEditorUtility.TryCaptureSharedRoots(
                    binder,
                    out BindingProfileEditorUtility.RootSnapshot sourceRoot,
                    out BindingProfileEditorUtility.RootSnapshot targetRoot,
                    out string rootError))
            {
                EditorUtility.DisplayDialog(
                    "Cannot Create Parameterized Profile",
                    rootError,
                    "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Create Binding Profile",
                "ViewDataBindingProfile",
                "asset",
                "Choose where to save the reusable binding profile.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            ViewDataBindingProfile profile = ScriptableObject.CreateInstance<ViewDataBindingProfile>();
            BindingProfileEditorUtility.CopyLocalBindingsToProfile(
                binder,
                profile,
                sourceRoot,
                targetRoot);
            AssetDatabase.CreateAsset(profile, path);
            BindingProfileEditorUtility.AddDisabledProfileReference(
                binder,
                profile,
                sourceRoot,
                targetRoot);
            AssetDatabase.SaveAssets();
            serializedObject.Update();

            EditorUtility.DisplayDialog(
                "Binding Profile Created",
                "The profile was added to this binder with its original Source and Target roots. The profile reference is disabled to prevent duplicate synchronization while the local bindings still exist. Remove or disable the local bindings, then enable the profile reference.",
                "OK");
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
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
            SerializedProperty writePolicyProperty = bindingProperty.FindPropertyRelative("writePolicy");
            SerializedProperty errorPolicyProperty = bindingProperty.FindPropertyRelative("errorPolicy");
            SerializedProperty sourceMissingPolicyProperty = bindingProperty.FindPropertyRelative("sourceMissingPolicy");
            SerializedProperty targetMissingPolicyProperty = bindingProperty.FindPropertyRelative("targetMissingPolicy");
            SerializedProperty retryIntervalProperty = bindingProperty.FindPropertyRelative("missingEndpointRetryInterval");
            SerializedProperty maximumRetryIntervalProperty = bindingProperty.FindPropertyRelative("maximumMissingEndpointRetryInterval");
            SerializedProperty alwaysEvaluateTransformationProperty = bindingProperty.FindPropertyRelative("alwaysEvaluateTransformation");
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

                    DrawBindingIndicator(bindingProperty, bindingIndex, enabledProperty.boolValue);
                    enabledProperty.boolValue = EditorGUILayout.Toggle(enabledProperty.boolValue, GUILayout.Width(18f));
                    nameProperty.stringValue = EditorGUILayout.TextField(nameProperty.stringValue, EditorStyles.boldLabel);

                    using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    {
                        if (GUILayout.Button("Sync", EditorStyles.miniButton, GUILayout.Width(42f)))
                        {
                            serializedObject.ApplyModifiedProperties();
                            binder.SynchronizeBinding(bindingIndex);
                            previewCache.Remove(foldoutKey);
                        }

                        if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(44f)))
                        {
                            binder.InvalidateBinding(bindingIndex);
                            previewCache.Remove(foldoutKey);
                        }
                    }

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
                DrawSettings(
                    directionProperty,
                    updateTimingProperty,
                    conflictProperty,
                    writePolicyProperty,
                    errorPolicyProperty,
                    sourceMissingPolicyProperty,
                    targetMissingPolicyProperty,
                    retryIntervalProperty,
                    maximumRetryIntervalProperty,
                    alwaysEvaluateTransformationProperty);
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
                DrawBindingPreview(foldoutKey, bindingIndex, direction);

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
            SerializedProperty conflictProperty,
            SerializedProperty writePolicyProperty,
            SerializedProperty errorPolicyProperty,
            SerializedProperty sourceMissingPolicyProperty,
            SerializedProperty targetMissingPolicyProperty,
            SerializedProperty retryIntervalProperty,
            SerializedProperty maximumRetryIntervalProperty,
            SerializedProperty alwaysEvaluateTransformationProperty)
        {
            EditorGUILayout.PropertyField(directionProperty, new GUIContent("Direction"));
            EditorGUILayout.PropertyField(updateTimingProperty, new GUIContent("Polling"));

            if ((BindingSyncDirection)directionProperty.enumValueIndex == BindingSyncDirection.TwoWay)
            {
                EditorGUILayout.PropertyField(conflictProperty, new GUIContent("Conflict Resolution"));
            }
            else
            {
                EditorGUILayout.PropertyField(
                    writePolicyProperty,
                    new GUIContent(
                        "Write Policy",
                        "When Value Changes avoids redundant setter calls. Always preserves authoritative polling semantics."));
            }

            EditorGUILayout.PropertyField(
                errorPolicyProperty,
                new GUIContent(
                    "Error Policy",
                    "Controls logging, runtime disabling, or exception behavior after a binding failure."));
            EditorGUILayout.PropertyField(
                sourceMissingPolicyProperty,
                new GUIContent("Missing Source", "Controls behavior when a Source instance or context disappears."));
            EditorGUILayout.PropertyField(
                targetMissingPolicyProperty,
                new GUIContent("Missing Target", "Controls behavior when the Target instance or context disappears."));
            EditorGUILayout.PropertyField(
                retryIntervalProperty,
                new GUIContent("Missing Retry Interval", "Initial delay between endpoint recovery attempts."));
            EditorGUILayout.PropertyField(
                maximumRetryIntervalProperty,
                new GUIContent("Maximum Retry Interval", "Maximum exponential backoff delay while an endpoint remains unavailable."));
            EditorGUILayout.PropertyField(
                alwaysEvaluateTransformationProperty,
                new GUIContent(
                    "Always Evaluate Transformation",
                    "Runs the formatter and converter even when raw inputs are unchanged. Enable only for stateful transformations."));
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
            SerializedProperty runtimeTypeProperty = instanceProperty.FindPropertyRelative("runtimeTypeName");

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

                case BindingInstanceKind.Runtime:
                    DrawRuntimeTypeSelector(runtimeTypeProperty);
                    EditorGUILayout.LabelField(
                        "Assign with BindingInstanceReference.SetRuntimeInstance. Sources can use the binder SetSourceInstance API.",
                        EditorStyles.wordWrappedMiniLabel);
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

        private void DrawRuntimeTypeSelector(SerializedProperty runtimeTypeProperty)
        {
            Type currentType = DefaultBindingInstanceResolver.FindType(runtimeTypeProperty.stringValue);
            string label = currentType == null ? "Select Runtime Type" : currentType.FullName;

            Rect buttonRect = EditorGUILayout.GetControlRect();
            buttonRect = EditorGUI.PrefixLabel(buttonRect, new GUIContent("Declared Type"));

            if (GUI.Button(buttonRect, label, BindingInspectorStyles.PathButtonStyle))
            {
                SerializedObject owner = serializedObject;
                string propertyPath = runtimeTypeProperty.propertyPath;
                PopupWindow.Show(
                    buttonRect,
                    new StaticTypePickerWindow(
                        type =>
                        {
                            owner.Update();
                            SerializedProperty property = owner.FindProperty(propertyPath);
                            if (property != null)
                            {
                                property.stringValue = type?.AssemblyQualifiedName ?? string.Empty;
                                owner.ApplyModifiedProperties();
                            }
                        },
                        false,
                        true));
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

        private void DrawBindingPreview(
            string bindingKey,
            int bindingIndex,
            BindingSyncDirection direction)
        {
            string previewKey = bindingKey + ":preview";
            if (!previewFoldouts.TryGetValue(previewKey, out bool expanded))
            {
                expanded = false;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    expanded = EditorGUILayout.Foldout(expanded, "Binding Preview", true);
                    previewFoldouts[previewKey] = expanded;
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(56f)))
                    {
                        serializedObject.ApplyModifiedProperties();
                        RefreshPreview(bindingKey, bindingIndex);
                    }
                }

                if (!expanded)
                {
                    return;
                }

                if (!previewCache.TryGetValue(bindingKey, out BindingPreview preview))
                {
                    serializedObject.ApplyModifiedProperties();
                    RefreshPreview(bindingKey, bindingIndex);
                    previewCache.TryGetValue(bindingKey, out preview);
                }

                DrawPreviewValue("Source", preview.SourceValue);
                if (direction != BindingSyncDirection.TargetToSource)
                {
                    DrawPreviewValue("Source -> Target", preview.ConvertedSourceValue);
                }

                DrawPreviewValue("Target", preview.TargetValue);
                if (direction != BindingSyncDirection.SourceToTarget)
                {
                    DrawPreviewValue("Target -> Source", preview.ConvertedTargetValue);
                }

                if (!preview.Result.IsSuccess)
                {
                    EditorGUILayout.HelpBox(
                        $"{preview.Result.Status}: {preview.Result.Message}",
                        MessageType.Warning);
                }
                else if (!string.IsNullOrEmpty(preview.Result.Message))
                {
                    EditorGUILayout.LabelField(
                        preview.Result.Message,
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void RefreshPreview(string bindingKey, int bindingIndex)
        {
            binder.TryGetPreview(bindingIndex, out BindingPreview preview);
            previewCache[bindingKey] = preview;
            Repaint();
        }

        private static void DrawPreviewValue(string label, object value)
        {
            EditorGUILayout.LabelField(
                label,
                GetPreviewText(value),
                EditorStyles.wordWrappedMiniLabel);
        }

        private static string GetPreviewText(object value)
        {
            const int maxPreviewLength = 256;

            if (value == null)
            {
                return "null";
            }

            if (value is UnityEngine.Object unityObject && unityObject == null)
            {
                return "null (destroyed Unity Object)";
            }

            try
            {
                string text = value.ToString() ?? "null";
                if (text.Length > maxPreviewLength)
                {
                    text = text.Substring(0, maxPreviewLength) + "...";
                }

                return $"{text}  ({GetFriendlyTypeName(value.GetType())})";
            }
            catch (Exception exception)
            {
                return $"ToString failed: {exception.Message}";
            }
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
            SerializedProperty target = bindingProperty.FindPropertyRelative("target");
            BindingSyncDirection direction = (BindingSyncDirection)bindingProperty
                .FindPropertyRelative("direction")
                .enumValueIndex;
            bool formatterEnabled = bindingProperty
                .FindPropertyRelative("formatter")
                .FindPropertyRelative("enabled")
                .boolValue;

            if (sources == null || sources.arraySize == 0)
            {
                error = "At least one Source is required.";
                return false;
            }

            if (formatterEnabled && direction != BindingSyncDirection.SourceToTarget)
            {
                error = "Formatters support Source -> Target only.";
                return false;
            }

            if (!formatterEnabled &&
                sources.arraySize != 1 &&
                BindingBackendRegistry.SourceBackend is SingleSourceBindingSourceBackend)
            {
                error = "Exactly one Source is required without a Formatter.";
                return false;
            }

            Type sourceOutputType = null;
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

                bool requireRead = direction != BindingSyncDirection.TargetToSource || formatterEnabled;
                bool requireWrite = direction != BindingSyncDirection.SourceToTarget;
                if (requireRead && !metadata.CanRead)
                {
                    error = $"Source {i + 1} is not readable.";
                    return false;
                }

                if (requireWrite && !metadata.CanWrite)
                {
                    error = $"Source {i + 1} is not writable.";
                    return false;
                }

                sourceMetadata.Add(metadata);
                if (!formatterEnabled && sources.arraySize == 1 && i == 0)
                {
                    sourceOutputType = metadata.ValueType;
                }
            }

            if (formatterEnabled)
            {
                SerializedProperty formatterProperty = bindingProperty.FindPropertyRelative("formatter");
                string formatterId = formatterProperty
                    .FindPropertyRelative("formatterId")
                    .stringValue;
                if (!BindingFormatterRegistry.TryGet(formatterId, out IBindingFormatter formatter))
                {
                    error = $"Formatter '{formatterId}' is not registered.";
                    return false;
                }

                if (!formatter.TryGetOutputType(sourceMetadata, out sourceOutputType, out error))
                {
                    return false;
                }
            }

            if (!BindingEditorResolver.TryGetMemberMetadata(
                    target,
                    out BindingMemberMetadata targetMetadata,
                    out error))
            {
                error = "Target: " + error;
                return false;
            }

            bool targetRequiresRead = direction != BindingSyncDirection.SourceToTarget;
            bool targetRequiresWrite = direction != BindingSyncDirection.TargetToSource;
            if (targetRequiresRead && !targetMetadata.CanRead)
            {
                error = "Target is not readable.";
                return false;
            }

            if (targetRequiresWrite && !targetMetadata.CanWrite)
            {
                error = "Target is not writable.";
                return false;
            }

            BindingConverter converter = bindingProperty
                .FindPropertyRelative("converter")
                .objectReferenceValue as BindingConverter;
            if (sourceOutputType != null)
            {
                if (converter == null && sourceOutputType != targetMetadata.ValueType)
                {
                    error = "Source and Target types require a Converter.";
                    return false;
                }

                if (converter != null &&
                    !converter.CanConvert(sourceOutputType, targetMetadata.ValueType))
                {
                    error = "The selected Converter is not compatible with the Source and Target types.";
                    return false;
                }

                if (converter != null &&
                    direction != BindingSyncDirection.SourceToTarget &&
                    !converter.CanConvertBack(targetMetadata.ValueType, sourceOutputType))
                {
                    error = "The selected Converter does not support reverse conversion.";
                    return false;
                }
            }

            BindingNullHandlingMode nullHandling = (BindingNullHandlingMode)bindingProperty
                .FindPropertyRelative("nullHandling")
                .enumValueIndex;
            bool fallbackEnabled = bindingProperty
                .FindPropertyRelative("fallback")
                .FindPropertyRelative("enabled")
                .boolValue;
            if (nullHandling == BindingNullHandlingMode.UseFallback && !fallbackEnabled)
            {
                error = "Null Handling requires an enabled Fallback.";
                return false;
            }

            error = string.Empty;
            return true;
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
            bindingProperty.FindPropertyRelative("writePolicy").enumValueIndex = (int)BindingWritePolicy.WhenValueChanges;
            bindingProperty.FindPropertyRelative("errorPolicy").enumValueIndex = (int)BindingErrorPolicy.ReportOnly;
            bindingProperty.FindPropertyRelative("sourceMissingPolicy").enumValueIndex = (int)MissingEndpointPolicy.ReportError;
            bindingProperty.FindPropertyRelative("targetMissingPolicy").enumValueIndex = (int)MissingEndpointPolicy.ReportError;
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
            instanceProperty.FindPropertyRelative("contextName").stringValue = BindingContextConstants.Default;
            instanceProperty.FindPropertyRelative("contextTypeName").stringValue = string.Empty;
            instanceProperty.FindPropertyRelative("runtimeTypeName").stringValue = string.Empty;
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

                case BindingInstanceKind.Context:
                    return !string.IsNullOrWhiteSpace(
                               instanceProperty.FindPropertyRelative("contextName").stringValue) ||
                           !string.IsNullOrWhiteSpace(
                               instanceProperty.FindPropertyRelative("contextTypeName").stringValue);

                case BindingInstanceKind.Runtime:
                    return !string.IsNullOrWhiteSpace(
                        instanceProperty.FindPropertyRelative("runtimeTypeName").stringValue);

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

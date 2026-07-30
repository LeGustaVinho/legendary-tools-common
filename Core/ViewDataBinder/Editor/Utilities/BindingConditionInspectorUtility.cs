using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.ViewBinding.Editor
{
    public static class BindingConditionInspectorUtility
    {
        public static void DrawExpression(
            SerializedProperty clauses,
            SerializedProperty sources)
        {
            if (clauses.arraySize == 0)
            {
                clauses.arraySize = 1;
                ResetClause(clauses.GetArrayElementAtIndex(0));
            }

            string[] labels = BuildSourceLabels(sources);
            for (int i = 0; i < clauses.arraySize; i++)
            {
                SerializedProperty clause = clauses.GetArrayElementAtIndex(i);
                SerializedProperty sourceIndex = clause.FindPropertyRelative("sourceIndex");
                sourceIndex.intValue = Mathf.Clamp(
                    sourceIndex.intValue,
                    0,
                    Mathf.Max(0, sources.arraySize - 1));

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"Clause {i + 1}", EditorStyles.miniBoldLabel);
                        if (i > 0)
                        {
                            EditorGUILayout.PropertyField(
                                clause.FindPropertyRelative("logicalOperator"),
                                GUIContent.none,
                                GUILayout.Width(70f));
                        }
                        clause.FindPropertyRelative("negate").boolValue = GUILayout.Toggle(
                            clause.FindPropertyRelative("negate").boolValue,
                            "NOT",
                            EditorStyles.miniButton,
                            GUILayout.Width(44f));
                        using (new EditorGUI.DisabledScope(clauses.arraySize <= 1))
                        {
                            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(24f)))
                            {
                                clauses.DeleteArrayElementAtIndex(i);
                                return;
                            }
                        }
                    }

                    sourceIndex.intValue = EditorGUILayout.Popup(
                        "Observed Source",
                        sourceIndex.intValue,
                        labels);
                    SerializedProperty comparison =
                        clause.FindPropertyRelative("comparisonOperator");
                    EditorGUILayout.PropertyField(comparison, new GUIContent("Operator"));
                    var comparisonOperator =
                        (EventBindingComparisonOperator)comparison.enumValueIndex;
                    if (EventBindingConditionEvaluator.RequiresComparisonValue(comparisonOperator))
                    {
                        BindingSerializedValueDrawer.Draw(
                            clause.FindPropertyRelative("comparisonValue"),
                            GetSourceType(sources, sourceIndex.intValue),
                            "Compare With");
                    }
                }
            }

            if (GUILayout.Button("+ Add Clause", EditorStyles.miniButton))
            {
                int index = clauses.arraySize++;
                ResetClause(clauses.GetArrayElementAtIndex(index));
            }
            EditorGUILayout.LabelField(
                BuildPreview(clauses, labels),
                EditorStyles.wordWrappedMiniLabel);
        }

        public static void ResetClause(SerializedProperty clause)
        {
            clause.FindPropertyRelative("sourceIndex").intValue = 0;
            clause.FindPropertyRelative("logicalOperator").enumValueIndex =
                (int)EventBindingLogicalOperator.And;
            clause.FindPropertyRelative("negate").boolValue = false;
            clause.FindPropertyRelative("comparisonOperator").enumValueIndex =
                (int)EventBindingComparisonOperator.Equal;
            ResetValue(clause.FindPropertyRelative("comparisonValue"));
        }

        public static void DrawValidation(
            SerializedProperty clauses,
            SerializedProperty sources)
        {
            if (clauses.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "A Condition requires at least one Clause.",
                    MessageType.Error);
                return;
            }

            for (int i = 0; i < clauses.arraySize; i++)
            {
                SerializedProperty clause = clauses.GetArrayElementAtIndex(i);
                int sourceIndex = clause.FindPropertyRelative("sourceIndex").intValue;
                if (sourceIndex < 0 || sourceIndex >= sources.arraySize)
                {
                    EditorGUILayout.HelpBox(
                        $"Clause {i + 1} references a Source that does not exist.",
                        MessageType.Error);
                    return;
                }

                SerializedProperty endpoint = sources.GetArrayElementAtIndex(sourceIndex)
                    .FindPropertyRelative("endpoint");
                if (!BindingEditorResolver.TryGetMemberMetadata(
                        endpoint,
                        out BindingMemberMetadata metadata,
                        out string error))
                {
                    EditorGUILayout.HelpBox(
                        $"Clause {i + 1}: {error}",
                        MessageType.Warning);
                    return;
                }
                if (!metadata.CanRead)
                {
                    EditorGUILayout.HelpBox(
                        $"Clause {i + 1}: Source {sourceIndex + 1} is not readable.",
                        MessageType.Error);
                    return;
                }

                var comparison = (EventBindingComparisonOperator)clause
                    .FindPropertyRelative("comparisonOperator").enumValueIndex;
                if (!EventBindingConditionEvaluator.IsOperatorSupported(
                        metadata.ValueType,
                        comparison))
                {
                    EditorGUILayout.HelpBox(
                        $"Clause {i + 1}: Type '{metadata.ValueType.Name}' does not support operator '{ObjectNames.NicifyVariableName(comparison.ToString())}'.",
                        MessageType.Error);
                    return;
                }
            }
        }

        private static string[] BuildSourceLabels(SerializedProperty sources)
        {
            var labels = new string[Mathf.Max(1, sources.arraySize)];
            if (sources.arraySize == 0)
            {
                labels[0] = "No Sources";
                return labels;
            }
            for (int i = 0; i < sources.arraySize; i++)
            {
                SerializedProperty endpoint = sources.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("endpoint");
                string path = endpoint.FindPropertyRelative("memberPath").stringValue;
                labels[i] = $"Source {i + 1}: " +
                            (string.IsNullOrWhiteSpace(path)
                                ? "Unselected"
                                : ComponentBindingPath.GetDisplayPath(path));
            }
            return labels;
        }

        private static Type GetSourceType(SerializedProperty sources, int index)
        {
            if (index < 0 || index >= sources.arraySize)
            {
                return null;
            }
            SerializedProperty endpoint = sources.GetArrayElementAtIndex(index)
                .FindPropertyRelative("endpoint");
            return BindingEditorResolver.TryGetMemberMetadata(
                    endpoint,
                    out BindingMemberMetadata metadata,
                    out _)
                ? metadata.ValueType
                : null;
        }

        private static string BuildPreview(
            SerializedProperty clauses,
            IReadOnlyList<string> labels)
        {
            var parts = new List<string>();
            for (int i = 0; i < clauses.arraySize; i++)
            {
                SerializedProperty clause = clauses.GetArrayElementAtIndex(i);
                if (i > 0)
                {
                    var logical = (EventBindingLogicalOperator)clause
                        .FindPropertyRelative("logicalOperator").enumValueIndex;
                    parts.Add(logical.ToString().ToUpperInvariant());
                }
                int sourceIndex = clause.FindPropertyRelative("sourceIndex").intValue;
                string source = sourceIndex >= 0 && sourceIndex < labels.Count
                    ? labels[sourceIndex]
                    : "Invalid Source";
                var comparison = (EventBindingComparisonOperator)clause
                    .FindPropertyRelative("comparisonOperator").enumValueIndex;
                string expression = $"{source} {ObjectNames.NicifyVariableName(comparison.ToString())}";
                if (clause.FindPropertyRelative("negate").boolValue)
                {
                    expression = $"NOT ({expression})";
                }
                parts.Add(expression);
            }
            return "Expression: " + string.Join(" ", parts);
        }

        private static void ResetValue(SerializedProperty value)
        {
            value.FindPropertyRelative("stringValue").stringValue = string.Empty;
            value.FindPropertyRelative("boolValue").boolValue = false;
            value.FindPropertyRelative("intValue").intValue = 0;
            value.FindPropertyRelative("longValue").longValue = 0L;
            value.FindPropertyRelative("floatValue").floatValue = 0f;
            value.FindPropertyRelative("doubleValue").doubleValue = 0d;
            value.FindPropertyRelative("vector2Value").vector2Value = Vector2.zero;
            value.FindPropertyRelative("vector3Value").vector3Value = Vector3.zero;
            value.FindPropertyRelative("vector4Value").vector4Value = Vector4.zero;
            value.FindPropertyRelative("colorValue").colorValue = Color.white;
            value.FindPropertyRelative("quaternionValue").quaternionValue = Quaternion.identity;
            value.FindPropertyRelative("objectValue").objectReferenceValue = null;
            value.FindPropertyRelative("serializedValue").stringValue = "0";
        }
    }
}

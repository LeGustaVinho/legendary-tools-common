using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace LegendaryTools.Editor
{
    [CustomPropertyDrawer(typeof(UniqueBehaviourReference))]
    public class UniqueBehaviourReferenceDrawer : PropertyDrawer
    {
        private static readonly Dictionary<string, UniqueBehaviour> UniqueBehaviourCache = new Dictionary<string, UniqueBehaviour>();

        /// <summary>
        /// Draws the property in 2 lines:
        ///   1) Either the ObjectField (if found or empty) or the "in Scene X" label + Reset
        ///   2) A text field for editing uniqueBehaviourId directly
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // Find your serialized properties
            SerializedProperty uniqueBehaviourIdProp = property.FindPropertyRelative("uniqueBehaviourId");
            SerializedProperty sceneIdProp = property.FindPropertyRelative("sceneId");
            SerializedProperty sceneNameProp = property.FindPropertyRelative("sceneName");
            SerializedProperty gameObjectNameProp = property.FindPropertyRelative("gameObjectName");

            // We split the overall 'position' into two rows:
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            Rect row1 = new Rect(position.x, position.y, position.width, lineHeight);
            Rect row2 = new Rect(
                position.x,
                position.y + lineHeight + spacing,
                position.width,
                lineHeight);

            // Figure out current UniqueBehaviour, if any
            UniqueBehaviour currentUniqueBehaviour = null;
            if (!string.IsNullOrEmpty(uniqueBehaviourIdProp.stringValue))
            {
                UniqueBehaviourCache.TryGetValue(uniqueBehaviourIdProp.stringValue, out currentUniqueBehaviour);

                if (currentUniqueBehaviour == null)
                {
                    if (UniqueBehaviour.TryGetValue(uniqueBehaviourIdProp.stringValue, out currentUniqueBehaviour))
                    {
                        UniqueBehaviourCache.AddOrUpdate(currentUniqueBehaviour.Guid, currentUniqueBehaviour);
                    }
                }

                if (currentUniqueBehaviour == null)
                {
                    UniqueBehaviour[] allUniqueBehaviours = 
                        UnityEngine.Object.FindObjectsByType<UniqueBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    currentUniqueBehaviour = Array.Find(allUniqueBehaviours,
                        item => item.Guid == uniqueBehaviourIdProp.stringValue);

                    if (currentUniqueBehaviour != null)
                    {
                        UniqueBehaviourCache.AddOrUpdate(currentUniqueBehaviour.Guid, currentUniqueBehaviour);
                    }
                }

                if (currentUniqueBehaviour == null)
                {
                    var allUniqueBehaviours = EditorExtensions.FindSceneObjectsOfType<UniqueBehaviour>();
                    var found = allUniqueBehaviours.Find(item => item.Item1.Guid == uniqueBehaviourIdProp.stringValue);
                    if (found.Item1 != null)
                    {
                        currentUniqueBehaviour = found.Item1;
                        UniqueBehaviourCache.AddOrUpdate(currentUniqueBehaviour.Guid, currentUniqueBehaviour);
                    }
                }
            }

            // First line logic
            if (currentUniqueBehaviour != null || string.IsNullOrEmpty(uniqueBehaviourIdProp.stringValue))
            {
                // If found (or empty), show the ObjectField
                UniqueBehaviour newUniqueBehaviour = (UniqueBehaviour)EditorGUI.ObjectField(
                    row1, label, currentUniqueBehaviour, typeof(UniqueBehaviour), true);

                if (newUniqueBehaviour != null)
                {
                    // If picking a prefab or something not in the scene, reset
                    if (!newUniqueBehaviour.gameObject.IsInScene())
                    {
                        ResetProperty(sceneIdProp, sceneNameProp, uniqueBehaviourIdProp, gameObjectNameProp);
                        newUniqueBehaviour = null;
                        Debug.LogWarning("[UniqueBehaviourReferenceDrawer] Only scene objects are allowed!");
                    }
                }

                if (newUniqueBehaviour != currentUniqueBehaviour)
                {
                    uniqueBehaviourIdProp.stringValue =
                        (newUniqueBehaviour != null) ? newUniqueBehaviour.Guid : string.Empty;

                    if (newUniqueBehaviour != null)
                    {
                        sceneIdProp.intValue = newUniqueBehaviour.gameObject.scene.buildIndex;
                        sceneNameProp.stringValue = newUniqueBehaviour.gameObject.scene.name;
                        gameObjectNameProp.stringValue = newUniqueBehaviour.gameObject.name;
                    }
                }
            }
            else
            {
                // If we have a GUID but can't find the object, show the old "in Scene X" label
                float col1Width = row1.width * 0.3f;
                float col2Width = row1.width * 0.6f;
                float col3Width = row1.width * 0.1f;

                Rect col1Rect = new Rect(row1.x, row1.y, col1Width, row1.height);
                Rect col2Rect = new Rect(col1Rect.xMax, row1.y, col2Width, row1.height);
                Rect col3Rect = new Rect(col2Rect.xMax, row1.y, col3Width, row1.height);

                EditorGUI.LabelField(col1Rect, label);
                EditorGUI.LabelField(col2Rect, new GUIContent(
                    $"{gameObjectNameProp.stringValue} ({uniqueBehaviourIdProp.stringValue}) " +
                    $"is in Scene {sceneNameProp.stringValue} ({sceneIdProp.intValue})"));

                if (GUI.Button(col3Rect, "Reset"))
                {
                    ResetProperty(sceneIdProp, sceneNameProp, uniqueBehaviourIdProp, gameObjectNameProp);
                }
            }

            // Second line: Always show a text field to edit the GUID directly
            EditorGUI.BeginChangeCheck();
            string newGuid = EditorGUI.TextField(row2, "Behaviour ID", uniqueBehaviourIdProp.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                uniqueBehaviourIdProp.stringValue = newGuid;
            }

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// We add an extra line for the GUID text field, so total 2 lines + spacing.
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
        }

        private static void ResetProperty(
            SerializedProperty sceneIdProp,
            SerializedProperty sceneNameProp,
            SerializedProperty uniqueBehaviourIdProp,
            SerializedProperty gameObjectNameProp)
        {
            sceneIdProp.intValue = -1;
            sceneNameProp.stringValue = string.Empty;
            uniqueBehaviourIdProp.stringValue = string.Empty;
            gameObjectNameProp.stringValue = string.Empty;
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    [CustomPropertyDrawer(typeof(UniqueBehaviourReference))]
    public class UniqueBehaviourReferenceDrawer : PropertyDrawer
    {
        private static readonly Dictionary<string, UniqueBehaviour> UniqueBehaviourCache = new Dictionary<string, UniqueBehaviour>();
        
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            SerializedProperty uniqueBehaviourIdProp = property.FindPropertyRelative("uniqueBehaviourId");
            SerializedProperty sceneIdProp = property.FindPropertyRelative("sceneId");
            SerializedProperty sceneNameProp = property.FindPropertyRelative("sceneName");
            SerializedProperty gameObjectNameProp = property.FindPropertyRelative("gameObjectName");
            
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
                        Object.FindObjectsByType<UniqueBehaviour>(FindObjectsInactive.Include,
                            FindObjectsSortMode.None);
                    currentUniqueBehaviour = Array.Find(allUniqueBehaviours,
                        item => item.Guid == uniqueBehaviourIdProp.stringValue);

                    if (currentUniqueBehaviour != null)
                    {
                        UniqueBehaviourCache.AddOrUpdate(currentUniqueBehaviour.Guid, currentUniqueBehaviour);
                    }
                }

                if (currentUniqueBehaviour == null)
                {
                    List<(UniqueBehaviour, GameObject)> allUniqueBehaviours =
                        EditorExtensions.FindSceneObjectsOfType<UniqueBehaviour>();
                    (UniqueBehaviour, GameObject) found =
                        allUniqueBehaviours.Find(item => item.Item1.Guid == uniqueBehaviourIdProp.stringValue);
                    if (found.Item1 != null)
                    {
                        currentUniqueBehaviour = found.Item1;
                        UniqueBehaviourCache.AddOrUpdate(currentUniqueBehaviour.Guid, currentUniqueBehaviour);
                    }
                }
            }

            if (currentUniqueBehaviour != null || string.IsNullOrEmpty(uniqueBehaviourIdProp.stringValue))
            {
                UniqueBehaviour newUniqueBehaviour = (UniqueBehaviour)EditorGUI.ObjectField(position, label,
                    currentUniqueBehaviour, typeof(UniqueBehaviour), true);
                
                if (newUniqueBehaviour != null)
                {
                    if (!newUniqueBehaviour.gameObject.IsInScene())
                    {
                        ResetProperty(sceneIdProp, sceneNameProp, uniqueBehaviourIdProp, gameObjectNameProp);
                        newUniqueBehaviour = null;
                        Debug.LogWarning("[UniqueBehaviourReferenceDrawer] Only scene objects is allowed !");
                    }
                }

                if (newUniqueBehaviour != currentUniqueBehaviour)
                {
                    uniqueBehaviourIdProp.stringValue = newUniqueBehaviour != null ? newUniqueBehaviour.Guid : string.Empty;

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
                float col1Width = position.width * 0.3f;
                float col2Width = position.width * 0.6f;
                float col3Width = position.width * 0.1f;

                Rect col1Rect = new Rect(position.x, position.y, col1Width, position.height);
                Rect col2Rect = new Rect(col1Rect.xMax, position.y, col2Width, position.height);
                Rect col3Rect = new Rect(col2Rect.xMax, position.y, col3Width, position.height);

                EditorGUI.LabelField(col1Rect, label);
                EditorGUI.LabelField(col2Rect, new GUIContent($"{gameObjectNameProp.stringValue} ({uniqueBehaviourIdProp.stringValue}) is in Scene {sceneNameProp.stringValue} ({sceneIdProp.intValue})"));
                if (GUI.Button(col3Rect, "Reset"))
                {
                    ResetProperty(sceneIdProp, sceneNameProp, uniqueBehaviourIdProp, gameObjectNameProp);
                }
            }
            
            EditorGUI.EndProperty();
        }

        private static void ResetProperty(SerializedProperty sceneIdProp, SerializedProperty sceneNameProp,
            SerializedProperty uniqueBehaviourIdProp, SerializedProperty gameObjectNameProp)
        {
            sceneIdProp.intValue = -1;
            sceneNameProp.stringValue = string.Empty;
            uniqueBehaviourIdProp.stringValue = string.Empty;
            gameObjectNameProp.stringValue = string.Empty;
        }
    }
}
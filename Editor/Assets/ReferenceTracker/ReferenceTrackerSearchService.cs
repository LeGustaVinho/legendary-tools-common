using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace LegendaryTools.Editor
{
    internal sealed class ReferenceTrackerSearchService
    {
        private readonly ReferenceTrackerScopeResolver _scopeResolver;

        public ReferenceTrackerSearchService(ReferenceTrackerScopeResolver scopeResolver)
        {
            _scopeResolver = scopeResolver;
        }

        public static bool IsSupportedTarget(UnityEngine.Object target)
        {
            return target is GameObject || target is Component;
        }

        public ReferenceTrackerSearchResult Search(UnityEngine.Object target, ReferenceTrackerSearchScope scopes)
        {
            ReferenceTrackerSearchResult result = new ReferenceTrackerSearchResult
            {
                Status = "Select a GameObject or Component target.",
                DurationMs = 0d,
            };

            if (!IsSupportedTarget(target))
            {
                return result;
            }

            string error;
            List<ReferenceTrackerScopeDescriptor> scopeDescriptors = _scopeResolver.Resolve(scopes, out error);
            if (!string.IsNullOrEmpty(error))
            {
                result.Status = error;
                return result;
            }

            ReferenceTrackerSearchTargetContext targetContext = BuildTargetContext(target);
            if (targetContext == null)
            {
                result.Status = "Unable to build the search target context.";
                return result;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < scopeDescriptors.Count; i++)
            {
                ScanScope(scopeDescriptors[i], targetContext, result.Usages);
            }

            stopwatch.Stop();
            result.DurationMs = stopwatch.Elapsed.TotalMilliseconds;

            SortResults(result.Usages);

            string scopeLabel = FormatScopeLabel(scopeDescriptors);
            result.Status = result.Usages.Count == 0
                ? string.Format("No references found in {0}.", scopeLabel)
                : string.Format("{0} reference(s) found in {1}.", result.Usages.Count, scopeLabel);

            return result;
        }

        private static ReferenceTrackerSearchTargetContext BuildTargetContext(UnityEngine.Object target)
        {
            ReferenceTrackerSearchTargetContext context = new ReferenceTrackerSearchTargetContext
            {
                OriginalTarget = target,
                TargetGameObject = target as GameObject,
                TargetComponent = target as Component,
            };

            if (context.TargetComponent != null)
            {
                context.TargetGameObject = context.TargetComponent.gameObject;
            }

            return context;
        }

        private static void ScanScope(
            ReferenceTrackerScopeDescriptor scopeDescriptor,
            ReferenceTrackerSearchTargetContext targetContext,
            List<ReferenceTrackerUsageResult> results)
        {
            GameObject[] rootObjects = scopeDescriptor.Scene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                GameObject root = rootObjects[i];
                if (root == null)
                {
                    continue;
                }

                Component[] components = root.GetComponentsInChildren<Component>(true);
                for (int j = 0; j < components.Length; j++)
                {
                    Component component = components[j];
                    if (component == null)
                    {
                        continue;
                    }

                    ScanComponent(component, targetContext, results);
                }
            }
        }

        private static void ScanComponent(
            Component hostComponent,
            ReferenceTrackerSearchTargetContext targetContext,
            List<ReferenceTrackerUsageResult> results)
        {
            SerializedObject serializedObject;

            try
            {
                serializedObject = new SerializedObject(hostComponent);
            }
            catch
            {
                return;
            }

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.Next(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                if (ShouldIgnoreProperty(iterator.propertyPath))
                {
                    continue;
                }

                UnityEngine.Object referencedObject = iterator.objectReferenceValue;
                if (referencedObject == null)
                {
                    continue;
                }

                string referenceTypeLabel;
                if (!IsMatch(targetContext, referencedObject, out referenceTypeLabel))
                {
                    continue;
                }

                results.Add(new ReferenceTrackerUsageResult
                {
                    HostGameObject = hostComponent.gameObject,
                    HostComponent = hostComponent,
                    HostGameObjectPath = ReferenceTrackerFormatting.GetHierarchyPath(hostComponent.gameObject),
                    HostComponentLabel = ReferenceTrackerFormatting.GetComponentLabel(hostComponent),
                    PropertyPath = iterator.propertyPath,
                    PropertyDisplayName = iterator.displayName,
                    ReferenceTypeLabel = referenceTypeLabel,
                    ReferencedObject = referencedObject,
                });
            }
        }

        private static bool IsMatch(
            ReferenceTrackerSearchTargetContext targetContext,
            UnityEngine.Object referencedObject,
            out string referenceTypeLabel)
        {
            referenceTypeLabel = string.Empty;

            if (targetContext.TargetComponent != null)
            {
                if (referencedObject == targetContext.TargetComponent)
                {
                    referenceTypeLabel = string.Format("Component ({0})", targetContext.TargetComponent.GetType().Name);
                    return true;
                }

                return false;
            }

            if (targetContext.TargetGameObject != null)
            {
                if (referencedObject == targetContext.TargetGameObject)
                {
                    referenceTypeLabel = "GameObject";
                    return true;
                }

                Component referencedComponent = referencedObject as Component;
                if (referencedComponent != null && referencedComponent.gameObject == targetContext.TargetGameObject)
                {
                    referenceTypeLabel = string.Format("Component ({0})", referencedComponent.GetType().Name);
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldIgnoreProperty(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
            {
                return true;
            }

            switch (propertyPath)
            {
                case "m_Script":
                case "m_GameObject":
                case "m_CorrespondingSourceObject":
                case "m_PrefabInstance":
                case "m_PrefabAsset":
                    return true;
                default:
                    return false;
            }
        }

        private static void SortResults(List<ReferenceTrackerUsageResult> results)
        {
            results.Sort(CompareResults);
        }

        internal static int CompareResults(ReferenceTrackerUsageResult a, ReferenceTrackerUsageResult b)
        {
            int byPath = string.Compare(a.HostGameObjectPath, b.HostGameObjectPath, StringComparison.OrdinalIgnoreCase);
            if (byPath != 0)
            {
                return byPath;
            }

            int byComponent = string.Compare(a.HostComponentLabel, b.HostComponentLabel, StringComparison.OrdinalIgnoreCase);
            if (byComponent != 0)
            {
                return byComponent;
            }

            return string.Compare(a.PropertyPath, b.PropertyPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatScopeLabel(List<ReferenceTrackerScopeDescriptor> scopeDescriptors)
        {
            string[] labels = new string[scopeDescriptors.Count];
            for (int i = 0; i < scopeDescriptors.Count; i++)
            {
                labels[i] = scopeDescriptors[i].Label;
            }

            return string.Join(", ", labels);
        }
    }
}

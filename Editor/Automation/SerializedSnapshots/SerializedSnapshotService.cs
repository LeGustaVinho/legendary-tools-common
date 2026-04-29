using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegendaryTools.Editor
{
    public static class SerializedSnapshotService
    {
        public static SerializedSnapshotRecord CaptureComponent(Component component, string snapshotName = null)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component));

            SerializedSnapshotRecord snapshot = CreateSnapshotRecord(
                component.gameObject,
                SerializedSnapshotScope.Component,
                component.GetType().AssemblyQualifiedName,
                snapshotName);

            SerializedComponentSnapshot componentSnapshot = CreateComponentSnapshot(component, component.transform);
            snapshot.Components.Add(componentSnapshot);
            snapshot.UnsupportedReferenceCount = componentSnapshot.ObjectReferences.Count(reference =>
                reference.Kind == SerializedSnapshotReferenceKind.Unsupported);

            return SerializedSnapshotLibrary.instance.Add(snapshot);
        }

        public static SerializedSnapshotRecord CaptureGameObject(GameObject gameObject, string snapshotName = null)
        {
            if (gameObject == null)
                throw new ArgumentNullException(nameof(gameObject));

            SerializedSnapshotRecord snapshot = CreateSnapshotRecord(
                gameObject,
                SerializedSnapshotScope.GameObject,
                null,
                snapshotName);

            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (component == null)
                    continue;

                snapshot.Components.Add(CreateComponentSnapshot(component, gameObject.transform));
            }

            snapshot.UnsupportedReferenceCount = snapshot.Components.Sum(component =>
                component.ObjectReferences.Count(reference =>
                    reference.Kind == SerializedSnapshotReferenceKind.Unsupported));

            return SerializedSnapshotLibrary.instance.Add(snapshot);
        }

        public static SerializedSnapshotPreview BuildPreview(SerializedSnapshotRecord snapshot, GameObject targetRoot)
        {
            SerializedSnapshotPreview preview = new()
            {
                TargetRoot = targetRoot
            };

            if (snapshot == null)
            {
                preview.Messages.Add("No snapshot selected.");
                return preview;
            }

            if (targetRoot == null)
            {
                preview.Messages.Add("Select a target GameObject to preview compatibility.");
                return preview;
            }

            foreach (SerializedComponentSnapshot componentSnapshot in snapshot.Components)
            {
                Component targetComponent = FindCompatibleComponent(targetRoot, componentSnapshot);
                if (targetComponent == null)
                {
                    preview.MissingComponents++;
                    preview.Messages.Add(
                        $"Missing component: {componentSnapshot.ComponentDisplayName} (index {componentSnapshot.ComponentOccurrenceIndex}).");
                    continue;
                }

                preview.MatchedComponents++;

                foreach (SerializedObjectReferenceSnapshot referenceSnapshot in componentSnapshot.ObjectReferences)
                {
                    if (referenceSnapshot.Kind == SerializedSnapshotReferenceKind.Unsupported)
                    {
                        preview.UnresolvableReferences++;
                        preview.Messages.Add(
                            $"Unsupported reference at {componentSnapshot.ComponentDisplayName}.{referenceSnapshot.PropertyPath}.");
                        continue;
                    }

                    if (ResolveReference(referenceSnapshot, targetRoot) != null ||
                        referenceSnapshot.Kind == SerializedSnapshotReferenceKind.Null)
                    {
                        preview.ResolvableReferences++;
                        continue;
                    }

                    preview.UnresolvableReferences++;
                    preview.Messages.Add(
                        $"Unresolved reference at {componentSnapshot.ComponentDisplayName}.{referenceSnapshot.PropertyPath}.");
                }
            }

            if (preview.Messages.Count == 0)
                preview.Messages.Add("Snapshot is compatible with the current selection.");

            return preview;
        }

        public static SerializedSnapshotApplyReport ApplyToGameObject(SerializedSnapshotRecord snapshot, GameObject targetRoot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            if (targetRoot == null)
                throw new ArgumentNullException(nameof(targetRoot));

            SerializedSnapshotApplyReport report = new();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Apply Snapshot {snapshot.Name}");

            try
            {
                foreach (SerializedComponentSnapshot componentSnapshot in snapshot.Components)
                {
                    Component targetComponent = FindCompatibleComponent(targetRoot, componentSnapshot);
                    if (targetComponent == null)
                    {
                        report.SkippedComponents++;
                        report.Messages.Add(
                            $"Skipped {componentSnapshot.ComponentDisplayName}: compatible component not found on {targetRoot.name}.");
                        continue;
                    }

                    if (ApplyComponentSnapshot(componentSnapshot, targetComponent, targetRoot, report))
                        report.AppliedComponents++;
                }
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            return report;
        }

        private static SerializedSnapshotRecord CreateSnapshotRecord(
            GameObject source,
            SerializedSnapshotScope scope,
            string sourceComponentTypeName,
            string snapshotName)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string scopeLabel = scope == SerializedSnapshotScope.Component && !string.IsNullOrEmpty(sourceComponentTypeName)
                ? GetDisplayName(sourceComponentTypeName)
                : "GameObject";

            return new SerializedSnapshotRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(snapshotName)
                    ? $"{source.name} - {scopeLabel} - {timestamp}"
                    : snapshotName.Trim(),
                Scope = scope,
                SourceScenePath = source.scene.path,
                SourceHierarchyPath = GetHierarchyPath(source.transform),
                SourceComponentTypeName = sourceComponentTypeName,
                SourcePrefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source),
                CapturedAtUtcTicks = DateTime.UtcNow.Ticks
            };
        }

        private static SerializedComponentSnapshot CreateComponentSnapshot(Component component, Transform root)
        {
            SerializedComponentSnapshot snapshot = new()
            {
                ComponentTypeName = component.GetType().AssemblyQualifiedName,
                ComponentDisplayName = component.GetType().Name,
                ComponentOccurrenceIndex = GetComponentOccurrenceIndex(component),
                JsonData = EditorJsonUtility.ToJson(component, true)
            };

            SerializedObject serializedObject = new(component);
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                if (iterator.propertyPath == "m_Script")
                {
                    enterChildren = true;
                    continue;
                }

                if (iterator.depth == 0)
                    snapshot.TopLevelPropertyCount++;

                snapshot.CapturedProperties.Add(CreateCapturedPropertySnapshot(iterator));

                if (iterator.propertyType == SerializedPropertyType.ObjectReference)
                    snapshot.ObjectReferences.Add(CaptureReference(iterator, root));

                enterChildren = true;
            }

            snapshot.ObjectReferenceCount = snapshot.ObjectReferences.Count;
            return snapshot;
        }

        private static SerializedCapturedPropertySnapshot CreateCapturedPropertySnapshot(SerializedProperty property)
        {
            return new SerializedCapturedPropertySnapshot
            {
                PropertyPath = property.propertyPath,
                DisplayName = property.displayName,
                PropertyTypeName = property.propertyType.ToString(),
                ValuePreview = GetPropertyPreview(property),
                Depth = property.depth
            };
        }

        private static SerializedObjectReferenceSnapshot CaptureReference(SerializedProperty property, Transform root)
        {
            Object referencedObject = property.objectReferenceValue;
            SerializedObjectReferenceSnapshot snapshot = new()
            {
                PropertyPath = property.propertyPath,
                DebugLabel = referencedObject != null
                    ? $"{referencedObject.name} ({referencedObject.GetType().Name})"
                    : "null"
            };

            if (referencedObject == null)
            {
                snapshot.Kind = SerializedSnapshotReferenceKind.Null;
                return snapshot;
            }

            if (TryCaptureAssetReference(referencedObject, snapshot) ||
                TryCaptureRelativeReference(referencedObject, root, snapshot) ||
                TryCaptureGlobalReference(referencedObject, snapshot))
            {
                return snapshot;
            }

            snapshot.Kind = SerializedSnapshotReferenceKind.Unsupported;
            return snapshot;
        }

        private static bool TryCaptureAssetReference(Object referencedObject, SerializedObjectReferenceSnapshot snapshot)
        {
            if (!AssetDatabase.Contains(referencedObject))
                return false;

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(referencedObject, out string guid, out long localId))
                return false;

            snapshot.Kind = SerializedSnapshotReferenceKind.Asset;
            snapshot.AssetGuid = guid;
            snapshot.AssetLocalId = localId;
            return true;
        }

        private static bool TryCaptureRelativeReference(
            Object referencedObject,
            Transform root,
            SerializedObjectReferenceSnapshot snapshot)
        {
            switch (referencedObject)
            {
                case GameObject referencedGameObject when IsChildOrSelf(root, referencedGameObject.transform):
                    snapshot.Kind = SerializedSnapshotReferenceKind.GameObject;
                    snapshot.RelativeGameObjectPath = GetRelativePath(root, referencedGameObject.transform);
                    return true;

                case Component referencedComponent when IsChildOrSelf(root, referencedComponent.transform):
                    snapshot.Kind = SerializedSnapshotReferenceKind.Component;
                    snapshot.RelativeGameObjectPath = GetRelativePath(root, referencedComponent.transform);
                    snapshot.ComponentTypeName = referencedComponent.GetType().AssemblyQualifiedName;
                    snapshot.ComponentOccurrenceIndex = GetComponentOccurrenceIndex(referencedComponent);
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryCaptureGlobalReference(
            Object referencedObject,
            SerializedObjectReferenceSnapshot snapshot)
        {
            try
            {
                string globalObjectId = GlobalObjectId.GetGlobalObjectIdSlow(referencedObject).ToString();
                if (string.IsNullOrWhiteSpace(globalObjectId))
                    return false;

                snapshot.Kind = SerializedSnapshotReferenceKind.GlobalObjectId;
                snapshot.GlobalObjectId = globalObjectId;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ApplyComponentSnapshot(
            SerializedComponentSnapshot componentSnapshot,
            Component targetComponent,
            GameObject targetRoot,
            SerializedSnapshotApplyReport report)
        {
            if (!TryCreateTemporaryComponent(componentSnapshot.ComponentTypeName, out GameObject tempGameObject,
                    out Component tempComponent, out string error))
            {
                report.SkippedComponents++;
                report.Messages.Add(
                    $"Skipped {componentSnapshot.ComponentDisplayName}: {error}");
                return false;
            }

            try
            {
                EditorJsonUtility.FromJsonOverwrite(componentSnapshot.JsonData, tempComponent);

                Undo.RecordObject(targetComponent, $"Apply Snapshot {componentSnapshot.ComponentDisplayName}");

                SerializedObject sourceObject = new(tempComponent);
                SerializedObject targetObject = new(targetComponent);
                sourceObject.UpdateIfRequiredOrScript();
                targetObject.UpdateIfRequiredOrScript();

                CopyTopLevelProperties(sourceObject, targetObject);

                foreach (SerializedObjectReferenceSnapshot referenceSnapshot in componentSnapshot.ObjectReferences)
                {
                    SerializedProperty targetProperty = targetObject.FindProperty(referenceSnapshot.PropertyPath);
                    if (targetProperty == null || targetProperty.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    Object resolvedReference = ResolveReference(referenceSnapshot, targetRoot);
                    if (resolvedReference == null && referenceSnapshot.Kind != SerializedSnapshotReferenceKind.Null)
                    {
                        report.FailedObjectReferences++;
                        report.Messages.Add(
                            $"Failed to resolve {componentSnapshot.ComponentDisplayName}.{referenceSnapshot.PropertyPath} ({referenceSnapshot.DebugLabel}).");
                    }
                    else
                    {
                        report.AppliedObjectReferences++;
                    }

                    targetProperty.objectReferenceValue = resolvedReference;
                }

                targetObject.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(targetComponent);
                EditorUtility.SetDirty(targetComponent);
                return true;
            }
            finally
            {
                if (tempGameObject != null)
                    Object.DestroyImmediate(tempGameObject);
            }
        }

        private static void CopyTopLevelProperties(SerializedObject sourceObject, SerializedObject targetObject)
        {
            SerializedProperty sourceIterator = sourceObject.GetIterator();
            bool enterChildren = true;

            while (sourceIterator.NextVisible(enterChildren))
            {
                enterChildren = true;

                if (sourceIterator.propertyPath == "m_Script" || sourceIterator.depth != 0)
                    continue;

                SerializedProperty targetProperty = targetObject.FindProperty(sourceIterator.propertyPath);
                if (targetProperty == null || targetProperty.propertyType != sourceIterator.propertyType)
                    continue;

                targetObject.CopyFromSerializedPropertyIfDifferent(sourceIterator);
            }
        }

        private static Object ResolveReference(SerializedObjectReferenceSnapshot snapshot, GameObject targetRoot)
        {
            switch (snapshot.Kind)
            {
                case SerializedSnapshotReferenceKind.Null:
                    return null;

                case SerializedSnapshotReferenceKind.Asset:
                    return LoadAssetReference(snapshot);

                case SerializedSnapshotReferenceKind.GameObject:
                    return FindRelativeGameObject(targetRoot.transform, snapshot.RelativeGameObjectPath);

                case SerializedSnapshotReferenceKind.Component:
                    return FindRelativeComponent(targetRoot.transform, snapshot);

                case SerializedSnapshotReferenceKind.GlobalObjectId:
                    if (GlobalObjectId.TryParse(snapshot.GlobalObjectId, out GlobalObjectId globalObjectId))
                        return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId);
                    return null;

                default:
                    return null;
            }
        }

        private static Object LoadAssetReference(SerializedObjectReferenceSnapshot snapshot)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(snapshot.AssetGuid);
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset == null)
                    continue;

                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId) &&
                    guid == snapshot.AssetGuid &&
                    localId == snapshot.AssetLocalId)
                {
                    return asset;
                }
            }

            return AssetDatabase.LoadAssetAtPath<Object>(assetPath);
        }

        private static GameObject FindRelativeGameObject(Transform root, string relativePath)
        {
            Transform transform = FindRelativeTransform(root, relativePath);
            return transform != null ? transform.gameObject : null;
        }

        private static Component FindRelativeComponent(Transform root, SerializedObjectReferenceSnapshot snapshot)
        {
            Transform referencedTransform = FindRelativeTransform(root, snapshot.RelativeGameObjectPath);
            if (referencedTransform == null)
                return null;

            Type componentType = Type.GetType(snapshot.ComponentTypeName);
            if (componentType == null)
                return null;

            Component[] components = referencedTransform.GetComponents(componentType);
            if (components.Length == 0)
                return null;

            return snapshot.ComponentOccurrenceIndex >= 0 && snapshot.ComponentOccurrenceIndex < components.Length
                ? components[snapshot.ComponentOccurrenceIndex]
                : components[0];
        }

        private static Transform FindRelativeTransform(Transform root, string relativePath)
        {
            if (root == null)
                return null;

            if (string.IsNullOrWhiteSpace(relativePath))
                return root;

            string[] segments = relativePath.Split('/');
            Transform current = root;

            foreach (string segment in segments)
            {
                current = current.Find(segment);
                if (current == null)
                    return null;
            }

            return current;
        }

        private static Component FindCompatibleComponent(GameObject targetRoot, SerializedComponentSnapshot snapshot)
        {
            Type componentType = Type.GetType(snapshot.ComponentTypeName);
            if (componentType == null)
                return null;

            Component[] components = targetRoot.GetComponents(componentType);
            if (components.Length == 0)
                return null;

            return snapshot.ComponentOccurrenceIndex >= 0 && snapshot.ComponentOccurrenceIndex < components.Length
                ? components[snapshot.ComponentOccurrenceIndex]
                : null;
        }

        private static bool TryCreateTemporaryComponent(
            string componentTypeName,
            out GameObject tempGameObject,
            out Component tempComponent,
            out string error)
        {
            tempGameObject = null;
            tempComponent = null;
            error = string.Empty;

            Type componentType = Type.GetType(componentTypeName);
            if (componentType == null)
            {
                error = $"type {componentTypeName} could not be resolved.";
                return false;
            }

            try
            {
                tempGameObject = componentType == typeof(RectTransform)
                    ? new GameObject("SerializedSnapshotTemp", typeof(RectTransform))
                    : new GameObject("SerializedSnapshotTemp");

                tempGameObject.hideFlags = HideFlags.HideAndDontSave;

                if (componentType == typeof(Transform) || componentType == typeof(RectTransform))
                {
                    tempComponent = tempGameObject.GetComponent(componentType);
                }
                else
                {
                    tempComponent = tempGameObject.AddComponent(componentType);
                }

                if (tempComponent == null)
                {
                    error = $"temporary component for {componentType.Name} could not be created.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                if (tempGameObject != null)
                    Object.DestroyImmediate(tempGameObject);

                tempGameObject = null;
                tempComponent = null;
                error = exception.Message;
                return false;
            }
        }

        private static int GetComponentOccurrenceIndex(Component component)
        {
            Component[] components = component.gameObject.GetComponents(component.GetType());
            for (int index = 0; index < components.Length; index++)
            {
                if (components[index] == component)
                    return index;
            }

            return 0;
        }

        private static bool IsChildOrSelf(Transform root, Transform candidate)
        {
            Transform current = candidate;
            while (current != null)
            {
                if (current == root)
                    return true;

                current = current.parent;
            }

            return false;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == target)
                return string.Empty;

            Stack<string> segments = new();
            Transform current = target;

            while (current != null && current != root)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", segments);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            Stack<string> segments = new();
            Transform current = transform;
            while (current != null)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", segments);
        }

        private static string GetDisplayName(string assemblyQualifiedTypeName)
        {
            Type type = Type.GetType(assemblyQualifiedTypeName);
            return type != null ? type.Name : assemblyQualifiedTypeName;
        }

        private static string GetPropertyPreview(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue.ToString();

                case SerializedPropertyType.Boolean:
                    return property.boolValue ? "true" : "false";

                case SerializedPropertyType.Float:
                    return property.floatValue.ToString("G9");

                case SerializedPropertyType.String:
                    return string.IsNullOrEmpty(property.stringValue) ? "\"\"" : $"\"{property.stringValue}\"";

                case SerializedPropertyType.Color:
                    return property.colorValue.ToString();

                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue != null
                        ? $"{property.objectReferenceValue.name} ({property.objectReferenceValue.GetType().Name})"
                        : "null";

                case SerializedPropertyType.LayerMask:
                    return property.intValue.ToString();

                case SerializedPropertyType.Enum:
                    return property.enumDisplayNames != null &&
                           property.enumValueIndex >= 0 &&
                           property.enumValueIndex < property.enumDisplayNames.Length
                        ? property.enumDisplayNames[property.enumValueIndex]
                        : property.enumValueIndex.ToString();

                case SerializedPropertyType.Vector2:
                    return property.vector2Value.ToString();

                case SerializedPropertyType.Vector3:
                    return property.vector3Value.ToString();

                case SerializedPropertyType.Vector4:
                    return property.vector4Value.ToString();

                case SerializedPropertyType.Rect:
                    return property.rectValue.ToString();

                case SerializedPropertyType.ArraySize:
                    return property.intValue.ToString();

                case SerializedPropertyType.Character:
                    return ((char)property.intValue).ToString();

                case SerializedPropertyType.AnimationCurve:
                    return property.animationCurveValue != null
                        ? $"Keys: {property.animationCurveValue.keys.Length}"
                        : "null";

                case SerializedPropertyType.Bounds:
                    return property.boundsValue.ToString();

                case SerializedPropertyType.Gradient:
                    return "<gradient>";

                case SerializedPropertyType.Quaternion:
                    return property.quaternionValue.eulerAngles.ToString();

                case SerializedPropertyType.ExposedReference:
                    return property.exposedReferenceValue != null
                        ? $"{property.exposedReferenceValue.name} ({property.exposedReferenceValue.GetType().Name})"
                        : "null";

                case SerializedPropertyType.FixedBufferSize:
                    return property.fixedBufferSize.ToString();

                case SerializedPropertyType.Vector2Int:
                    return property.vector2IntValue.ToString();

                case SerializedPropertyType.Vector3Int:
                    return property.vector3IntValue.ToString();

                case SerializedPropertyType.RectInt:
                    return property.rectIntValue.ToString();

                case SerializedPropertyType.BoundsInt:
                    return property.boundsIntValue.ToString();

                case SerializedPropertyType.ManagedReference:
                    return string.IsNullOrWhiteSpace(property.managedReferenceFullTypename)
                        ? "null"
                        : property.managedReferenceFullTypename;

                case SerializedPropertyType.Hash128:
                    return property.hash128Value.ToString();

                case SerializedPropertyType.Generic:
                    if (property.isArray)
                        return $"Size: {property.arraySize}";

                    return property.hasVisibleChildren ? "{...}" : string.Empty;

                default:
                    return $"<{property.propertyType}>";
            }
        }
    }
}

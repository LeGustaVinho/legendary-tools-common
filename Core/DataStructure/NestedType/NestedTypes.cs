using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace LegendaryTools
{
    [CreateAssetMenu(menuName = "Tools/LegendaryTools/Create NestedTypes", fileName = "NestedTypes", order = 0)]
    public class NestedTypes : ScriptableObject, ISerializationCallbackReceiver
    {
        [SerializeField] private List<NestedType> nestedTypes = new();

        // Cache dictionaries for hierarchy relationships
        private readonly Dictionary<NestedType, NestedType[]> childrenCache = new();
        private readonly Dictionary<NestedType, NestedType[]> childrenRecursiveCache = new();
        private readonly Dictionary<NestedType, NestedType[]> parentsRecursiveCache = new();
        private readonly Dictionary<NestedType, NestedType[]> leafNodesCache = new();
        private readonly Dictionary<NestedType, int> depthCache = new();
        private readonly Dictionary<NestedType, NestedType[]> siblingsCache = new();
        private bool isCacheValid = false;
        private bool isBuildingCache = false;

        /// <summary>
        /// Gets a value indicating whether the cache is currently being built.
        /// </summary>
        internal bool IsBuildingCache => isBuildingCache;

        /// <summary>
        /// Gets all NestedTypes in this collection.
        /// </summary>
        public IReadOnlyList<NestedType> AllNestedTypes => nestedTypes.AsReadOnly();

        /// <summary>
        /// Adds a NestedType to the collection and sets its container reference.
        /// </summary>
        /// <param name="nestedType">The NestedType to add.</param>
        /// <exception cref="ArgumentNullException">Thrown when nestedType is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when nestedType has duplicate ID or creates circular reference.</exception>
        public void AddNestedType(NestedType nestedType)
        {
            if (nestedType == null) throw new ArgumentNullException(nameof(nestedType));

            if (nestedTypes.Any(t => t.Id == nestedType.Id))
                throw new InvalidOperationException("A NestedType with the same ID already exists.");

            // Temporarily add to check for circular references
            nestedTypes.Add(nestedType);
            if (HasCircularDependencies(nestedType))
            {
                nestedTypes.Remove(nestedType);
                throw new InvalidOperationException("Adding this NestedType would create a circular dependency.");
            }

            nestedType.Container = this;
            InvalidateCache();
        }

        /// <summary>
        /// Invalidates the hierarchy cache, forcing a rebuild on next access.
        /// </summary>
        public void InvalidateCache()
        {
            isCacheValid = false;
            childrenCache.Clear();
            childrenRecursiveCache.Clear();
            parentsRecursiveCache.Clear();
            leafNodesCache.Clear();
            depthCache.Clear();
            siblingsCache.Clear();
        }

        /// <summary>
        /// Rebuilds the hierarchy cache if it is invalid.
        /// </summary>
        private void RebuildCacheIfNeeded()
        {
            if (isCacheValid) return;

            isBuildingCache = true;
            try
            {
                // Temporary cache to store children during rebuild
                Dictionary<NestedType, List<NestedType>> tempChildrenCache = new();

                // Initialize children for each NestedType
                foreach (NestedType type in nestedTypes)
                {
                    tempChildrenCache[type] = new List<NestedType>();
                }

                // Populate direct children
                foreach (NestedType type in nestedTypes)
                {
                    if (type?.Parents != null)
                        foreach (NestedType parent in type.Parents)
                        {
                            if (parent != null && tempChildrenCache.ContainsKey(parent))
                                tempChildrenCache[parent].Add(type);
                        }
                }

                // Convert temporary cache to final children cache
                foreach (NestedType type in nestedTypes)
                {
                    childrenCache[type] = tempChildrenCache[type].ToArray();
                }

                // Populate recursive caches
                foreach (NestedType type in nestedTypes)
                {
                    // Cache recursive children
                    List<NestedType> allChildren = new();
                    CollectChildrenRecursive(type, allChildren, new HashSet<NestedType>());
                    childrenRecursiveCache[type] = allChildren.ToArray();

                    // Cache recursive parents
                    List<NestedType> allParents = new();
                    CollectParentsRecursive(type, allParents, new HashSet<NestedType>());
                    parentsRecursiveCache[type] = allParents.ToArray();

                    // Cache leaf nodes
                    leafNodesCache[type] = childrenRecursiveCache[type].Where(t => t.IsLeafType).ToArray();

                    // Cache siblings
                    siblingsCache[type] = GetSiblingsWithSameParentsInternal(type);
                }

                // Calculate depths in a separate pass
                CalculateDepths();
            }
            finally
            {
                isBuildingCache = false;
            }

            isCacheValid = true;
        }

        /// <summary>
        /// Calculates depths for all NestedTypes iteratively.
        /// </summary>
        private void CalculateDepths()
        {
            // Initialize depths to 0
            foreach (NestedType type in nestedTypes)
            {
                depthCache[type] = 0;
            }

            // Iterate until no more depth changes (handles multi-parent hierarchies)
            bool changed;
            do
            {
                changed = false;
                foreach (NestedType type in nestedTypes)
                {
                    NestedType[] parents = type?.Parents ?? Array.Empty<NestedType>();
                    if (parents.Any())
                    {
                        int maxParentDepth = parents.Where(p => p != null && depthCache.ContainsKey(p))
                            .Max(p => depthCache[p]);
                        int newDepth = maxParentDepth + 1;
                        if (depthCache[type] != newDepth)
                        {
                            depthCache[type] = newDepth;
                            changed = true;
                        }
                    }
                }
            } while (changed);
        }

        /// <summary>
        /// Collects all children recursively for cache building.
        /// </summary>
        /// <param name="nestedType">The NestedType to process.</param>
        /// <param name="result">The list to store children.</param>
        /// <param name="visited">Set of visited types to prevent circular references.</param>
        private void CollectChildrenRecursive(NestedType nestedType, List<NestedType> result,
            HashSet<NestedType> visited)
        {
            if (nestedType == null || visited.Contains(nestedType)) return;

            visited.Add(nestedType);
            NestedType[] children = childrenCache.ContainsKey(nestedType)
                ? childrenCache[nestedType]
                : Array.Empty<NestedType>();
            result.AddRange(children);

            foreach (NestedType child in children)
            {
                CollectChildrenRecursive(child, result, visited);
            }
        }

        /// <summary>
        /// Collects all parents recursively for cache building.
        /// </summary>
        /// <param name="nestedType">The NestedType to process.</param>
        /// <param name="result">The list to store parents.</param>
        /// <param name="visited">Set of visited types to prevent circular references.</param>
        private void CollectParentsRecursive(NestedType nestedType, List<NestedType> result,
            HashSet<NestedType> visited)
        {
            if (nestedType == null || visited.Contains(nestedType)) return;

            visited.Add(nestedType);
            NestedType[] parents = nestedType.Parents ?? Array.Empty<NestedType>();
            result.AddRange(parents);

            foreach (NestedType parent in parents)
            {
                CollectParentsRecursive(parent, result, visited);
            }
        }

        /// <summary>
        /// Gets all direct children of the specified NestedType from the cache (used during cache building).
        /// </summary>
        /// <param name="nestedType">The NestedType to get children for.</param>
        /// <returns>An array of direct child NestedTypes.</returns>
        internal NestedType[] GetChildrenFromCache(NestedType nestedType)
        {
            return childrenCache.TryGetValue(nestedType, out NestedType[] children)
                ? children
                : Array.Empty<NestedType>();
        }

        /// <summary>
        /// Gets all direct children of the specified NestedType (internal non-cached version).
        /// </summary>
        /// <param name="nestedType">The NestedType to get children for.</param>
        /// <returns>An array of direct child NestedTypes.</returns>
        private NestedType[] GetChildrenInternal(NestedType nestedType)
        {
            if (nestedType == null) return Array.Empty<NestedType>();

            // Directly query nestedTypes to avoid cache-related recursion
            return nestedTypes
                .Where(t => t != null && t.Parents != null && t.Parents.Contains(nestedType))
                .ToArray();
        }

        /// <summary>
        /// Gets all direct children of the specified NestedType.
        /// </summary>
        /// <param name="nestedType">The NestedType to get children for.</param>
        /// <returns>An array of direct child NestedTypes.</returns>
        public NestedType[] GetChildren(NestedType nestedType)
        {
            RebuildCacheIfNeeded();
            return childrenCache.TryGetValue(nestedType, out NestedType[] children)
                ? children
                : Array.Empty<NestedType>();
        }

        /// <summary>
        /// Gets all children of the specified NestedType recursively.
        /// </summary>
        /// <param name="nestedType">The NestedType to get children for.</param>
        /// <returns>An array of all descendant NestedTypes.</returns>
        public NestedType[] GetChildrenRecursive(NestedType nestedType)
        {
            RebuildCacheIfNeeded();
            return childrenRecursiveCache.TryGetValue(nestedType, out NestedType[] children)
                ? children
                : Array.Empty<NestedType>();
        }

        /// <summary>
        /// Gets all direct parents of the specified NestedType.
        /// </summary>
        /// <param name="nestedType">The NestedType to get parents for.</param>
        /// <returns>An array of direct parent NestedTypes.</returns>
        public NestedType[] GetParents(NestedType nestedType)
        {
            return nestedType?.Parents ?? Array.Empty<NestedType>();
        }

        /// <summary>
        /// Gets all parents of the specified NestedType recursively.
        /// </summary>
        /// <param name="nestedType">The NestedType to get parents for.</param>
        /// <returns>An array of all ancestor NestedTypes.</returns>
        public NestedType[] GetParentsRecursive(NestedType nestedType)
        {
            RebuildCacheIfNeeded();
            return parentsRecursiveCache.TryGetValue(nestedType, out NestedType[] parents)
                ? parents
                : Array.Empty<NestedType>();
        }

        /// <summary>
        /// Checks if a NestedType is a child of another NestedType.
        /// </summary>
        /// <param name="child">The potential child NestedType.</param>
        /// <param name="parent">The potential parent NestedType.</param>
        /// <returns>True if child is a descendant of parent; otherwise, false.</returns>
        public bool IsChildOf(NestedType child, NestedType parent)
        {
            if (child == null || parent == null) return false;

            RebuildCacheIfNeeded();
            return childrenRecursiveCache.TryGetValue(parent, out NestedType[] children) && children.Contains(child);
        }

        /// <summary>
        /// Gets all leaf nodes (NestedTypes with no children) under the specified NestedType.
        /// </summary>
        /// <param name="nestedType">The NestedType to get leaf nodes for.</param>
        /// <returns>An array of leaf node NestedTypes.</returns>
        public NestedType[] GetLeafNodes(NestedType nestedType)
        {
            RebuildCacheIfNeeded();
            return leafNodesCache.TryGetValue(nestedType, out NestedType[] leafNodes)
                ? leafNodes
                : Array.Empty<NestedType>();
        }

        /// <summary>
        /// Gets all NestedTypes that share the same parents as the specified NestedType (internal non-cached version).
        /// </summary>
        /// <param name="nestedType">The NestedType to find siblings for.</param>
        /// <returns>An array of NestedTypes sharing the same parents.</returns>
        private NestedType[] GetSiblingsWithSameParentsInternal(NestedType nestedType)
        {
            if (nestedType == null || nestedType.Parents == null) return Array.Empty<NestedType>();

            return nestedTypes
                .Where(t => t != null && t != nestedType && t.Parents != null &&
                            t.Parents.SequenceEqual(nestedType.Parents))
                .ToArray();
        }

        /// <summary>
        /// Gets all NestedTypes that share the same parents as the specified NestedType.
        /// </summary>
        /// <param name="nestedType">The NestedType to find siblings for.</param>
        /// <returns>An array of NestedTypes sharing the same parents.</returns>
        public NestedType[] GetSiblingsWithSameParents(NestedType nestedType)
        {
            RebuildCacheIfNeeded();
            return siblingsCache.TryGetValue(nestedType, out NestedType[] siblings)
                ? siblings
                : Array.Empty<NestedType>();
        }

        /// <summary>
        /// Gets the depth of the specified NestedType in the hierarchy (internal non-cached version).
        /// </summary>
        /// <param name="nestedType">The NestedType to get depth for.</param>
        /// <returns>The depth from the furthest root.</returns>
        private int GetDepthInternal(NestedType nestedType)
        {
            if (nestedType == null) return 0;

            return depthCache.TryGetValue(nestedType, out int depth) ? depth : 0;
        }

        /// <summary>
        /// Gets the depth of the specified NestedType in the hierarchy.
        /// </summary>
        /// <param name="nestedType">The NestedType to get depth for.</param>
        /// <returns>The depth from the furthest root.</returns>
        public int GetDepth(NestedType nestedType)
        {
            RebuildCacheIfNeeded();
            return depthCache.TryGetValue(nestedType, out int depth) ? depth : 0;
        }

        /// <summary>
        /// Finds the closest common ancestor between two NestedTypes.
        /// </summary>
        /// <param name="type1">The first NestedType.</param>
        /// <param name="type2">The second NestedType.</param>
        /// <returns>The closest common ancestor, or null if none exists.</returns>
        public NestedType FindClosestCommonAncestor(NestedType type1, NestedType type2)
        {
            if (type1 == null || type2 == null) return null;

            RebuildCacheIfNeeded();
            List<NestedType> parents1 = parentsRecursiveCache.TryGetValue(type1, out NestedType[] p1)
                ? p1.Concat(new[] { type1 }).ToList()
                : new List<NestedType> { type1 };
            List<NestedType> parents2 = parentsRecursiveCache.TryGetValue(type2, out NestedType[] p2)
                ? p2.Concat(new[] { type2 }).ToList()
                : new List<NestedType> { type2 };

            return parents1.Intersect(parents2).FirstOrDefault();
        }

        /// <summary>
        /// Checks if the hierarchy contains circular dependencies starting from the specified NestedType.
        /// </summary>
        /// <param name="nestedType">The NestedType to check.</param>
        /// <returns>True if circular dependencies exist; otherwise, false.</returns>
        public bool HasCircularDependencies(NestedType nestedType)
        {
            if (nestedType == null) return false;

            HashSet<NestedType> visited = new();
            Stack<NestedType> stack = new();
            stack.Push(nestedType);

            while (stack.Count > 0)
            {
                NestedType current = stack.Pop();
                if (visited.Contains(current)) return true;

                visited.Add(current);
                NestedType[] parents = current?.Parents ?? Array.Empty<NestedType>();
                foreach (NestedType parent in parents)
                {
                    if (parent != null) stack.Push(parent);
                }
            }

            return false;
        }

        /// <summary>
        /// Returns a string representation of the hierarchy.
        /// </summary>
        /// <returns>A string representing the hierarchy.</returns>
        public string GetHierarchyAsText()
        {
            StringBuilder result = new();
            List<NestedType> roots = nestedTypes.Where(t => t != null && t.IsRoot).ToList();

            foreach (NestedType root in roots)
            {
                BuildHierarchyText(root, result, 0);
            }

            return result.ToString();
        }

        /// <summary>
        /// Builds the hierarchy text for a NestedType and its children recursively.
        /// </summary>
        /// <param name="nestedType">The NestedType to process.</param>
        /// <param name="builder">The StringBuilder to append to.</param>
        /// <param name="depth">The current depth in the hierarchy.</param>
        private void BuildHierarchyText(NestedType nestedType, StringBuilder builder, int depth)
        {
            if (nestedType == null) return;

            builder.AppendLine(new string(' ', depth * 2) + nestedType.DisplayName);
            foreach (NestedType child in GetChildren(nestedType))
            {
                BuildHierarchyText(child, builder, depth + 1);
            }
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            InvalidateCache(); // Ensure cache is rebuilt after deserialization
        }
    }
}
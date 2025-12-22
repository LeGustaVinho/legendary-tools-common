using System;
using System.Collections.Generic;
using LegendaryTools.GenericExpressionEngine;
using LegendaryTools.GraphV2;

namespace LegendaryTools.AttributeSystemV2
{
    /// <summary>
    /// Scope relation provider that navigates an EntityScopeTree (Tree) to resolve relations like:
    /// - parent / owner
    /// - root
    /// - child (only if there is exactly one child)
    /// - child0, child1, ...
    /// - child_<ScopeName>
    /// - firstChild / lastChild
    /// - nextSibling / prevSibling
    /// </summary>
    public sealed class EntityTreeScopeRelationProvider<T> : IScopeRelationProvider<T>
    {
        public EntityScopeTree OwnerTree { get; }

        public EntityTreeScopeRelationProvider(EntityScopeTree ownerTree)
        {
            OwnerTree = ownerTree ?? throw new ArgumentNullException(nameof(ownerTree));
        }

        public bool TryResolveRelatedScope(
            EvaluationContext<T> context,
            string fromScopeName,
            string relationName,
            out string targetScopeName)
        {
            targetScopeName = null;

            if (string.IsNullOrWhiteSpace(fromScopeName) || string.IsNullOrWhiteSpace(relationName))
                return false;

            if (!OwnerTree.TryGetNode(fromScopeName, out EntityScopeNode fromNode) || fromNode == null)
                return false;

            relationName = relationName.Trim();

            // Identity
            if (relationName.Equals("self", StringComparison.OrdinalIgnoreCase))
            {
                targetScopeName = fromScopeName;
                return true;
            }

            // Root
            if (relationName.Equals("root", StringComparison.OrdinalIgnoreCase))
            {
                EntityScopeNode root = OwnerTree.RootNode;
                if (root == null) return false;
                targetScopeName = "root";
                return true;
            }

            // Parent / owner
            if (relationName.Equals("parent", StringComparison.OrdinalIgnoreCase) ||
                relationName.Equals("owner", StringComparison.OrdinalIgnoreCase))
            {
                ITreeNode parent = (fromNode as ITreeNode)?.ParentNode;
                if (parent is EntityScopeNode parentEntityNode)
                {
                    targetScopeName = parentEntityNode.ScopeName;
                    return true;
                }

                return false;
            }

            // Children
            if (relationName.Equals("child", StringComparison.OrdinalIgnoreCase))
            {
                // Only valid if exactly one child (avoid ambiguity).
                if ((fromNode as ITreeNode)?.ChildNodes?.Count == 1 &&
                    (fromNode as ITreeNode).ChildNodes[0] is EntityScopeNode onlyChild)
                {
                    targetScopeName = onlyChild.ScopeName;
                    return true;
                }

                return false;
            }

            if (relationName.Equals("firstChild", StringComparison.OrdinalIgnoreCase))
            {
                List<ITreeNode> children = (fromNode as ITreeNode)?.ChildNodes;
                if (children != null && children.Count > 0 && children[0] is EntityScopeNode first)
                {
                    targetScopeName = first.ScopeName;
                    return true;
                }

                return false;
            }

            if (relationName.Equals("lastChild", StringComparison.OrdinalIgnoreCase))
            {
                List<ITreeNode> children = (fromNode as ITreeNode)?.ChildNodes;
                if (children != null && children.Count > 0 && children[children.Count - 1] is EntityScopeNode last)
                {
                    targetScopeName = last.ScopeName;
                    return true;
                }

                return false;
            }

            // child0, child1...
            if (relationName.StartsWith("child", StringComparison.OrdinalIgnoreCase))
            {
                // child_<ScopeName>
                int underscore = relationName.IndexOf('_');
                if (underscore >= 0 && underscore < relationName.Length - 1)
                {
                    string childScope = relationName.Substring(underscore + 1);
                    List<ITreeNode> children = (fromNode as ITreeNode)?.ChildNodes;
                    if (children == null) return false;

                    for (int i = 0; i < children.Count; i++)
                    {
                        if (children[i] is EntityScopeNode c &&
                            c.ScopeName.Equals(childScope, StringComparison.OrdinalIgnoreCase))
                        {
                            targetScopeName = c.ScopeName;
                            return true;
                        }
                    }

                    return false;
                }

                // childN
                string tail = relationName.Substring("child".Length);
                if (int.TryParse(tail, out int index))
                {
                    List<ITreeNode> children = (fromNode as ITreeNode)?.ChildNodes;
                    if (children == null) return false;

                    if (index >= 0 && index < children.Count && children[index] is EntityScopeNode c)
                    {
                        targetScopeName = c.ScopeName;
                        return true;
                    }
                }

                return false;
            }

            // Siblings
            if (relationName.Equals("nextSibling", StringComparison.OrdinalIgnoreCase) ||
                relationName.Equals("prevSibling", StringComparison.OrdinalIgnoreCase))
            {
                ITreeNode parent = (fromNode as ITreeNode)?.ParentNode;
                if (parent == null) return false;

                List<ITreeNode> siblings = parent.ChildNodes;
                int idx = siblings.IndexOf(fromNode);
                if (idx < 0) return false;

                int nextIdx = relationName.Equals("nextSibling", StringComparison.OrdinalIgnoreCase)
                    ? idx + 1
                    : idx - 1;
                if (nextIdx < 0 || nextIdx >= siblings.Count) return false;

                if (siblings[nextIdx] is EntityScopeNode sib)
                {
                    targetScopeName = sib.ScopeName;
                    return true;
                }

                return false;
            }

            return false;
        }
    }
}
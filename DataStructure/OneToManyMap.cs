using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryTools
{
    /// <summary>
    ///     A generic data structure representing a one-to-many relationship.
    ///     Provides batch operations, enumeration of relationships, query methods,
    ///     update operations, and notifications via Action delegates.
    /// </summary>
    public class OneToManyMap<TParent, TChild>
    {
        // Internal storage for relationships.
        private readonly Dictionary<TParent, List<TChild>> parentToChildren = new Dictionary<TParent, List<TChild>>();
        private readonly Dictionary<TChild, TParent> childToParent = new Dictionary<TChild, TParent>();

        #region Notification Actions

        /// <summary>
        ///     Invoked when a new parent is added.
        /// </summary>
        public event Action<TParent> ParentAdded;

        /// <summary>
        ///     Invoked when a parent is removed.
        /// </summary>
        public event Action<TParent> ParentRemoved;

        /// <summary>
        ///     Invoked when a new (parent, child) relationship is established.
        /// </summary>
        public event Action<TParent, TChild> RelationshipAdded;

        /// <summary>
        ///     Invoked when a (parent, child) relationship is removed.
        /// </summary>
        public event Action<TParent, TChild> RelationshipRemoved;

        #endregion

        #region Properties

        /// <summary>
        ///     Gets all the parent keys.
        /// </summary>
        public IEnumerable<TParent> Parents => parentToChildren.Keys;

        /// <summary>
        ///     Gets all the child keys.
        /// </summary>
        public IEnumerable<TChild> Children => childToParent.Keys;

        /// <summary>
        ///     Enumerates all (parent, child) relationships.
        /// </summary>
        public IEnumerable<(TParent Parent, TChild Child)> Relationships
        {
            get
            {
                foreach (KeyValuePair<TParent, List<TChild>> kvp in parentToChildren)
                {
                    TParent parent = kvp.Key;
                    foreach (TChild child in kvp.Value)
                    {
                        yield return (parent, child);
                    }
                }
            }
        }

        #endregion

        #region Basic Operations

        /// <summary>
        ///     Adds a parent if it doesn't already exist.
        /// </summary>
        public void AddParent(TParent parent)
        {
            if (!parentToChildren.ContainsKey(parent))
            {
                parentToChildren.Add(parent, new List<TChild>());
                ParentAdded?.Invoke(parent);
            }
        }

        /// <summary>
        ///     Checks whether the specified parent exists.
        /// </summary>
        public bool ContainsParent(TParent parent)
        {
            return parentToChildren.ContainsKey(parent);
        }

        /// <summary>
        ///     Checks whether the specified child exists.
        /// </summary>
        public bool ContainsChild(TChild child)
        {
            return childToParent.ContainsKey(child);
        }

        /// <summary>
        ///     Adds a single (parent, child) relationship.
        ///     Automatically adds the parent if needed.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if the child already exists.</exception>
        public void Add(TParent parent, TChild child)
        {
            if (!parentToChildren.TryGetValue(parent, out List<TChild> children))
            {
                children = new List<TChild>();
                parentToChildren.Add(parent, children);
                ParentAdded?.Invoke(parent);
            }

            if (childToParent.ContainsKey(child))
            {
                throw new ArgumentException("Child already exists in the map.");
            }

            children.Add(child);
            childToParent.Add(child, parent);
            RelationshipAdded?.Invoke(parent, child);
        }

        #endregion

        #region Batch Operations

        /// <summary>
        ///     Adds multiple children to the specified parent.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if any child already exists.</exception>
        public void AddRange(TParent parent, IEnumerable<TChild> children)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));

            if (!parentToChildren.TryGetValue(parent, out List<TChild> childList))
            {
                childList = new List<TChild>();
                parentToChildren.Add(parent, childList);
                ParentAdded?.Invoke(parent);
            }

            foreach (TChild child in children)
            {
                if (childToParent.ContainsKey(child))
                {
                    throw new ArgumentException("One of the children already exists in the map.");
                }

                childList.Add(child);
                childToParent.Add(child, parent);
                RelationshipAdded?.Invoke(parent, child);
            }
        }

        /// <summary>
        ///     Removes multiple children from the specified parent.
        ///     If a parent's child list becomes empty, the parent is removed.
        /// </summary>
        public void RemoveRange(TParent parent, IEnumerable<TChild> childrenToRemove)
        {
            if (childrenToRemove == null) throw new ArgumentNullException(nameof(childrenToRemove));

            if (!parentToChildren.TryGetValue(parent, out List<TChild> children))
            {
                throw new KeyNotFoundException("Parent not found in the map.");
            }

            // Convert to list to avoid modifying the collection during iteration.
            foreach (TChild child in childrenToRemove.ToList())
            {
                if (childToParent.TryGetValue(child, out TParent currentParent) &&
                    currentParent.Equals(parent))
                {
                    children.Remove(child);
                    childToParent.Remove(child);
                    RelationshipRemoved?.Invoke(parent, child);
                }
            }

            if (children.Count == 0)
            {
                parentToChildren.Remove(parent);
                ParentRemoved?.Invoke(parent);
            }
        }

        #endregion

        #region Update Operations

        /// <summary>
        ///     Reassigns a child from its current parent to a new parent.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Thrown if the child is not found.</exception>
        public void ReassignChild(TChild child, TParent newParent)
        {
            if (!childToParent.TryGetValue(child, out TParent currentParent))
            {
                throw new KeyNotFoundException("Child not found in the map.");
            }

            if (currentParent.Equals(newParent))
            {
                return; // No change needed.
            }

            // Remove the child from the current parent's list.
            List<TChild> currentChildren = parentToChildren[currentParent];
            currentChildren.Remove(child);
            RelationshipRemoved?.Invoke(currentParent, child);

            // Remove the current parent if it has no more children.
            if (currentChildren.Count == 0)
            {
                parentToChildren.Remove(currentParent);
                ParentRemoved?.Invoke(currentParent);
            }

            // Add the child to the new parent's list.
            if (!parentToChildren.TryGetValue(newParent, out List<TChild> newChildren))
            {
                newChildren = new List<TChild>();
                parentToChildren.Add(newParent, newChildren);
                ParentAdded?.Invoke(newParent);
            }

            newChildren.Add(child);
            childToParent[child] = newParent;
            RelationshipAdded?.Invoke(newParent, child);
        }

        #endregion

        #region Query by Predicate

        /// <summary>
        ///     Finds all parents that satisfy the specified predicate.
        /// </summary>
        public IEnumerable<TParent> FindParents(Func<TParent, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return parentToChildren.Keys.Where(predicate);
        }

        /// <summary>
        ///     Finds all children that satisfy the specified predicate.
        /// </summary>
        public IEnumerable<TChild> FindChildren(Func<TChild, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return childToParent.Keys.Where(predicate);
        }

        /// <summary>
        ///     Finds all (parent, child) relationships that satisfy the specified predicate.
        /// </summary>
        public IEnumerable<(TParent Parent, TChild Child)> FindRelationships(Func<TParent, TChild, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            foreach (KeyValuePair<TParent, List<TChild>> kvp in parentToChildren)
            {
                TParent parent = kvp.Key;
                foreach (TChild child in kvp.Value)
                {
                    if (predicate(parent, child))
                    {
                        yield return (parent, child);
                    }
                }
            }
        }

        #endregion

        #region Other Basic Operations

        /// <summary>
        ///     Removes the specified parent and all its associated children.
        /// </summary>
        public bool RemoveParent(TParent parent)
        {
            if (!parentToChildren.TryGetValue(parent, out List<TChild> children))
            {
                return false;
            }

            // Remove each relationship.
            foreach (TChild child in children.ToList())
            {
                childToParent.Remove(child);
                RelationshipRemoved?.Invoke(parent, child);
            }

            parentToChildren.Remove(parent);
            ParentRemoved?.Invoke(parent);
            return true;
        }

        /// <summary>
        ///     Removes the specified child and its relationship.
        /// </summary>
        public bool RemoveChild(TChild child)
        {
            if (!childToParent.TryGetValue(child, out TParent parent))
            {
                return false;
            }

            List<TChild> children = parentToChildren[parent];
            if (children.Remove(child))
            {
                childToParent.Remove(child);
                RelationshipRemoved?.Invoke(parent, child);
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Gets the parent associated with the given child.
        /// </summary>
        public TParent GetParentOfChild(TChild child)
        {
            if (childToParent.TryGetValue(child, out TParent parent))
            {
                return parent;
            }

            throw new KeyNotFoundException("Child not found in the map.");
        }

        /// <summary>
        ///     Gets the list of children associated with the given parent.
        /// </summary>
        public IEnumerable<TChild> GetChildrenOfParent(TParent parent)
        {
            if (parentToChildren.TryGetValue(parent, out List<TChild> children))
            {
                return children;
            }

            throw new KeyNotFoundException("Parent not found in the map.");
        }

        #endregion
    }
}
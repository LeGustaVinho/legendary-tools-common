using System;
using System.Collections.Generic;

namespace LegendaryTools
{
    /// <summary>
    /// A generic data structure to map one-to-many relationships.
    /// In our example, TParent might be a Species and TChild a Population.
    /// </summary>
    public class OneToManyMap<TParent, TChild>
    {
        // Maps each parent to its list of children.
        private readonly Dictionary<TParent, List<TChild>> parentToChildren;

        // Maps each child to its parent.
        private readonly Dictionary<TChild, TParent> childToParent;

        public OneToManyMap()
        {
            parentToChildren = new Dictionary<TParent, List<TChild>>();
            childToParent = new Dictionary<TChild, TParent>();
        }

        /// <summary>
        /// Gets all parents (e.g. all species).
        /// </summary>
        public IEnumerable<TParent> Parents => parentToChildren.Keys;

        /// <summary>
        /// Gets all children (e.g. the entire population).
        /// </summary>
        public IEnumerable<TChild> Children => childToParent.Keys;

        /// <summary>
        /// Adds a new parent to the structure if it does not already exist.
        /// </summary>
        public void AddParent(TParent parent)
        {
            if (!parentToChildren.ContainsKey(parent))
            {
                parentToChildren.Add(parent, new List<TChild>());
            }
        }

        /// <summary>
        /// Determines whether the specified parent exists.
        /// </summary>
        public bool ContainsParent(TParent parent)
        {
            return parentToChildren.ContainsKey(parent);
        }

        /// <summary>
        /// Determines whether the specified child exists.
        /// </summary>
        public bool ContainsChild(TChild child)
        {
            return childToParent.ContainsKey(child);
        }

        /// <summary>
        /// Adds a child to the specified parent. If the parent does not exist yet,
        /// it is automatically added.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown if the child is already present in the structure.
        /// </exception>
        public void Add(TParent parent, TChild child)
        {
            // Ensure the parent is present.
            if (!parentToChildren.TryGetValue(parent, out List<TChild> children))
            {
                children = new List<TChild>();
                parentToChildren.Add(parent, children);
            }
            // If the child already exists, throw an exception.
            if (childToParent.ContainsKey(child))
            {
                throw new ArgumentException("Child already exists in the map.");
            }
            children.Add(child);
            childToParent.Add(child, parent);
        }

        /// <summary>
        /// Removes a parent and all its associated children from the structure.
        /// </summary>
        /// <returns>True if the parent existed and was removed; otherwise, false.</returns>
        public bool RemoveParent(TParent parent)
        {
            if (!parentToChildren.TryGetValue(parent, out List<TChild> children))
                return false;

            // Remove all children associated with this parent.
            foreach (var child in children)
            {
                childToParent.Remove(child);
            }
            parentToChildren.Remove(parent);
            return true;
        }

        /// <summary>
        /// Removes the specified child from the structure.
        /// </summary>
        /// <returns>True if the child existed and was removed; otherwise, false.</returns>
        public bool RemoveChild(TChild child)
        {
            if (!childToParent.TryGetValue(child, out TParent parent))
                return false;

            // Remove the child from its parent's list.
            List<TChild> children = parentToChildren[parent];
            children.Remove(child);
            childToParent.Remove(child);
            return true;
        }

        /// <summary>
        /// Retrieves the parent associated with the given child.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Thrown if the child is not found.</exception>
        public TParent GetParentOfChild(TChild child)
        {
            if (childToParent.TryGetValue(child, out TParent parent))
                return parent;

            throw new KeyNotFoundException("Child not found in the map.");
        }

        /// <summary>
        /// Retrieves all the children associated with the given parent.
        /// </summary>
        /// <exception cref="KeyNotFoundException">Thrown if the parent is not found.</exception>
        public IEnumerable<TChild> GetChildrenOfParent(TParent parent)
        {
            if (parentToChildren.TryGetValue(parent, out List<TChild> children))
                return children;

            throw new KeyNotFoundException("Parent not found in the map.");
        }

        /// <summary>
        /// Tries to retrieve the parent for the given child.
        /// </summary>
        public bool TryGetParentOfChild(TChild child, out TParent parent)
        {
            return childToParent.TryGetValue(child, out parent);
        }

        /// <summary>
        /// Tries to retrieve the children for the given parent.
        /// </summary>
        public bool TryGetChildrenOfParent(TParent parent, out List<TChild> children)
        {
            return parentToChildren.TryGetValue(parent, out children);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Graph
{
    public class Branch<G, N> : HierarchicalNode<G, N>, INode<N>, ICollection<N>
        where G : Tree<G, N>
        where N : Branch<G, N>
    {
        public List<N> Children { get; } = new List<N>();

        public N Parent { get; private set; }

        public N[] BranchHierarchy
        {
            get
            {
                List<N> path = new List<N>();
                for (N parent = Parent; parent != null; parent = parent.Parent)
                {
                    if (parent != null)
                    {
                        path.Add(parent);
                    }
                }

                path.Reverse();
                return path.ToArray();
            }
        }

        public bool IsReadOnly { get; } = false;

        protected Branch() : base()
        {
        }

        public Branch(G owner) : base(owner)
        {
        }
        
        public void Add(N newBranch)
        {
            if (newBranch == null)
            {
                Debug.LogError("[Branch:Add()] -> Branch cannot be null.");
                return;
            }
            
            if (!Children.Contains(newBranch))
            {
                Children.Add(newBranch);
                newBranch.Parent = this as N;
            }
            else
            {
                Debug.LogError("[Branch:Add()] -> Already contains this branch.");
            }
        }

        public void Clear()
        {
            foreach (N child in Children)
            {
                child.Parent = null;
            }
            
            Children.Clear();
        }

        public void CopyTo(N[] array, int arrayIndex)
        {
            Children.CopyTo(array, arrayIndex);
        }

        public bool Remove(N newBranch)
        {
            if (newBranch == null)
            {
                Debug.LogError("[Branch:Remove()] -> Branch cannot be null.");
                return false;
            }

            if (Children.Contains(newBranch))
            {
                newBranch.Parent = null;
            }
            
            return Children.Remove(newBranch);
        }

        public bool Contains(N newBranch)
        {
            return Children.Contains(newBranch);
        }

        public N Find(Predicate<N> match)
        {
            return GetAllChildrenNodes().Find(match);
        }
        
        public List<N> FindAll(Predicate<N> match)
        {
            return GetAllChildrenNodes().FindAll(match);
        }

        public List<N> GetAllChildrenNodes()
        {
            List<N> result = new List<N>();
            GetAllChildrenNodesInternal(result);
            return result;
        }
        
        private List<N> GetAllChildrenNodesInternal (List<N> result)
        {
            if (Children.Count == 0)
            {
                return result;
            }

            result.AddRange(Children);
            foreach (N child in Children)
            {
                child.GetAllChildrenNodesInternal(result);
            }

            return result;
        }
        
        public IEnumerator<N> GetEnumerator()
        {
            return Children.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        
        public IEnumerator<N> GetAllChildrenEnumerator()
        {
            return GetAllChildrenNodes().GetEnumerator();
        }

        public virtual N[] Neighbours
        {
            get
            {
                List<N> neighbours = new List<N>();
                neighbours.AddRange(Children);
                neighbours.Add(Parent);
                return neighbours.ToArray();
            }
        }

        public int Count => Children.Count;

        public void SetParent(N newParent)
        {
            if (newParent == null)
            {
                if (Parent != null)
                {
                    Parent.Children.Remove(this as N);
                    Parent = null;
                }
            }
            else
            {
                Parent = newParent;
                Parent.Children.Add(this as N);
            }
        }
    }
}
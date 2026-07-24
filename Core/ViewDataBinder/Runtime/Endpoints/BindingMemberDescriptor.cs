using System;
using System.Collections.Generic;

namespace LegendaryTools.ViewBinding
{
    public sealed class BindingMemberDescriptor
    {
        private readonly Func<IReadOnlyList<BindingMemberDescriptor>> childrenFactory;
        private IReadOnlyList<BindingMemberDescriptor> children;

        public BindingMemberDescriptor(
            string name,
            string path,
            Type valueType,
            bool canRead,
            bool canWrite,
            bool canExpand,
            Func<IReadOnlyList<BindingMemberDescriptor>> childrenFactory)
        {
            Name = name;
            Path = path;
            ValueType = valueType;
            CanRead = canRead;
            CanWrite = canWrite;
            CanExpand = canExpand;
            this.childrenFactory = childrenFactory;
        }

        public string Name { get; }

        public string Path { get; }

        public Type ValueType { get; }

        public bool CanRead { get; }

        public bool CanWrite { get; }

        public bool CanExpand { get; }

        public IReadOnlyList<BindingMemberDescriptor> Children
        {
            get
            {
                if (children == null)
                {
                    children = childrenFactory != null
                        ? childrenFactory()
                        : Array.Empty<BindingMemberDescriptor>();
                }

                return children;
            }
        }
    }
}

using System;

namespace LegendaryTools.ViewBinding
{
    public readonly struct BindingMemberMetadata
    {
        public BindingMemberMetadata(Type valueType, bool canRead, bool canWrite)
        {
            ValueType = valueType;
            CanRead = canRead;
            CanWrite = canWrite;
        }

        public Type ValueType { get; }

        public bool CanRead { get; }

        public bool CanWrite { get; }
    }
}

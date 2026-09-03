using System.Reflection;

namespace FlatData
{
    public sealed class FlattenOptions
    {
        public FlattenOptions()
        {
            IncludeFields = true;
            IncludeProperties = true;
            IncludeRootTypeName = true;
            PreserveNullValues = true;
            MaximumDepth = 32;
            BindingFlags = BindingFlags.Instance | BindingFlags.Public;
        }

        public bool IncludeFields { get; set; }

        public bool IncludeProperties { get; set; }

        public bool IncludeRootTypeName { get; set; }

        public bool PreserveNullValues { get; set; }

        public int MaximumDepth { get; set; }

        public BindingFlags BindingFlags { get; set; }
    }
}

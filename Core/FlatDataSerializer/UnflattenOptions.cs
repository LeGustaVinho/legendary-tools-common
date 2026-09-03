namespace FlatData
{
    public sealed class UnflattenOptions
    {
        public UnflattenOptions()
        {
            IncludeRootTypeName = true;
            ObjectFactory = new DefaultObjectFactory();
            ValueConverter = new DefaultValueConverter();
        }

        public bool IncludeRootTypeName { get; set; }

        public IObjectFactory ObjectFactory { get; set; }

        public IValueConverter ValueConverter { get; set; }
    }
}

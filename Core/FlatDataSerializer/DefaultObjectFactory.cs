using System;

namespace FlatData
{
    public sealed class DefaultObjectFactory : IObjectFactory
    {
        public object Create(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (type.IsAbstract || type.IsInterface)
            {
                throw new InvalidOperationException(
                    $"Cannot create an instance of abstract or interface type '{type.FullName}'.");
            }

            try
            {
                return Activator.CreateInstance(type);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Could not create an instance of '{type.FullName}'. " +
                    "The type must have a public parameterless constructor or a custom object factory.",
                    exception);
            }
        }
    }
}

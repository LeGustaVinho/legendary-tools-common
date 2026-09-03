using System;

namespace FlatData
{
    public interface IObjectFactory
    {
        object Create(Type type);
    }
}

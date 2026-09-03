using System;

namespace FlatData
{
    public interface IValueConverter
    {
        object Convert(object value, Type targetType);
    }
}

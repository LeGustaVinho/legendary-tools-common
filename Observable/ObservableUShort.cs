using System;

namespace LegendaryTools
{
    [Serializable]
    public class ObservableUShort : Observable<ushort>, IFormattable
    {
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return value.ToString(format, formatProvider);
        }
    }
}
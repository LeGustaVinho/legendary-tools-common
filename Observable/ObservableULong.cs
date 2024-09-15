using System;

namespace LegendaryTools
{
    [Serializable]
    public class ObservableULong : Observable<ulong>, IFormattable
    {
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return value.ToString(format, formatProvider);
        }
    }
}
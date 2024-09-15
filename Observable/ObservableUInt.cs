using System;

namespace LegendaryTools
{
    [Serializable]
    public class ObservableUInt : Observable<uint>, IFormattable
    {
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return value.ToString(format, formatProvider);
        }
    }
}
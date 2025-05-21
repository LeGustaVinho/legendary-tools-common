using System;

namespace LegendaryTools
{
    [Serializable]
    public class ObservableLong : Observable<long>, IFormattable
    {
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return value.ToString(format, formatProvider);
        }
    }
}
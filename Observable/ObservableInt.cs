using System;

namespace LegendaryTools
{
    [Serializable]
    public class ObservableInt : Observable<int>, IFormattable
    {
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return value.ToString(format, formatProvider);
        }
    }
}
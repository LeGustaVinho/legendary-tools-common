using System;

namespace LegendaryTools
{
    [Serializable]
    public class ObservableFloat : Observable<float>, IFormattable
    {
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return value.ToString(format, formatProvider);
        }
    }
}
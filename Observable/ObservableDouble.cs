using System;

namespace LegendaryTools
{
    [Serializable]
    public class ObservableDouble : Observable<double>, IFormattable
    {
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return value.ToString(format, formatProvider);
        }
    }
}
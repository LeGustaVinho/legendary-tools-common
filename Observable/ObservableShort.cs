using System;

namespace LegendaryTools
{
    [Serializable]
    public class ObservableShort : Observable<short>, IFormattable
    {
        public string ToString(string format, IFormatProvider formatProvider)
        {
            return value.ToString(format, formatProvider);
        }
    }
}
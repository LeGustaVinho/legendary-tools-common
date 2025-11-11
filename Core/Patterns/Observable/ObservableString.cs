using System;
using System.Collections;
using System.Collections.Generic;

namespace LegendaryTools
{
    [Serializable]
    public class ObservableString : Observable<string>,
        IEnumerable<char>,
        ICloneable
    {
        public IEnumerator<char> GetEnumerator()
        {
            // Null-safe enumeration
            return (Value ?? string.Empty).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)(Value ?? string.Empty)).GetEnumerator();
        }

        public object Clone()
        {
            return new ObservableString { value = value };
        }
    }
}
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
            return Value.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return (Value as IEnumerable).GetEnumerator();
        }

        public object Clone()
        {
            return new ObservableString()
            {
                value = value
            };
        }
    }
}
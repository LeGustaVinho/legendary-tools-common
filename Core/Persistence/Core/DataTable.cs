using System;
using System.Collections.Generic;

namespace LegendaryTools.Persistence
{
    [Serializable]
    public class DataTable
    {
        public Type Type;
        public int Version;
        public int Revision;
        public DateTime Timestamp;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
        [Sirenix.OdinInspector.TableList(ShowIndexLabels = true)]
#endif
        public Dictionary<string, object> IdentifiedEntries = new Dictionary<string, object>();

        public DataTable(Type type, int version, int revision, DateTime timestamp)
        {
            Type = type;
            Version = version;
            Revision = revision;
            Timestamp = timestamp;
        }
    }
}
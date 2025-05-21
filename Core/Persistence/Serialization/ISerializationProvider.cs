using System;
using System.Collections.Generic;

namespace LegendaryTools.Persistence
{
    public interface ISerializationProvider
    {
        public string Extension { get; }
        
        public object Serialize(Dictionary<Type, DataTable> dataTable);

        public Dictionary<Type, DataTable> Deserialize(object serializedData);
    }
    
    public interface IStringSerializationProvider : ISerializationProvider
    {
        public new string Serialize(Dictionary<Type, DataTable> dataTable);

        public Dictionary<Type, DataTable> Deserialize(string serializedData);
    }
    
    public interface IBinarySerializationProvider : ISerializationProvider
    {
        public new byte[] Serialize(Dictionary<Type, DataTable> dataTable);

        public Dictionary<Type, DataTable> Deserialize(byte[] serializedData);
    }
}
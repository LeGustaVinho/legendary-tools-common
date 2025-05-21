using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace LegendaryTools.Persistence
{
    [CreateAssetMenu(menuName = "Tools/Persistence/BinaryProvider", fileName = "BinaryProvider", order = 0)]
    public class BinaryProvider : ScriptableObject, IBinarySerializationProvider
    {
        public string Extension => "bin";
        public virtual byte[] Serialize(Dictionary<Type, DataTable> dataTable)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            using (MemoryStream stream = new MemoryStream())
            {
                formatter.Serialize(stream, dataTable);
                return stream.ToArray();
            }
        }
        
        public virtual Dictionary<Type, DataTable> Deserialize(byte[] serializedData)
        {
            if (serializedData.Length == 0) return new Dictionary<Type, DataTable>();
            
            BinaryFormatter formatter = new BinaryFormatter();
            using (MemoryStream stream = new MemoryStream(serializedData))
            {
                return formatter.Deserialize(stream) as Dictionary<Type, DataTable>;
            }
        }

        object ISerializationProvider.Serialize(Dictionary<Type, DataTable> dataTable)
        {
            return this.Serialize(dataTable);
        }
        
        public Dictionary<Type, DataTable> Deserialize(object serializedData)
        {
            return Deserialize(serializedData as byte[]);
        }
    }
}
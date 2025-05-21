using System;
using System.Collections.Generic;
#if NIDO_SERIALIZER
using OdinSerializer;
#endif
using UnityEngine;

namespace LegendaryTools.Persistence
{
    [CreateAssetMenu(menuName = "Tools/Persistence/OdinBinaryProvider", fileName = "OdinBinaryProvider", order = 0)]
    public class OdinBinaryProvider : ScriptableObject, IBinarySerializationProvider
    {
        public string Extension => "odin.bin";
        
        object ISerializationProvider.Serialize(Dictionary<Type, DataTable> dataTable)
        {
            return ((IBinarySerializationProvider)this).Serialize(dataTable);
        }

        public Dictionary<Type, DataTable> Deserialize(object serializedData)
        {
            return Deserialize(serializedData as byte[]);
        }

        public Dictionary<Type, DataTable> Deserialize(byte[] serializedData)
        {
#if NIDO_SERIALIZER
            if (serializedData.Length == 0) return new Dictionary<Type, DataTable>();
            return SerializationUtility.DeserializeValue<Dictionary<Type, DataTable>>(serializedData, 
                DataFormat.Binary);
#else
            return null;
#endif
        }

        byte[] IBinarySerializationProvider.Serialize(Dictionary<Type, DataTable> dataTable)
        {
#if NIDO_SERIALIZER
            return SerializationUtility.SerializeValue(dataTable, DataFormat.Binary);
#else
            return null;
#endif
        }
    }
}
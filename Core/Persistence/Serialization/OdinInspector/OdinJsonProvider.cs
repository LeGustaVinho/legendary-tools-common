using System;
using System.Collections.Generic;
using System.Text;
#if ODIN_INSPECTOR
using Sirenix.Serialization;
#endif
using UnityEngine;

namespace LegendaryTools.Persistence
{
    [CreateAssetMenu(menuName = "Tools/Persistence/OdinJsonProvider", fileName = "OdinJsonProvider", order = 0)]
    public class OdinJsonProvider : ScriptableObject, IStringSerializationProvider
    {
        public string Extension => "odin.json";
        object ISerializationProvider.Serialize(Dictionary<Type, DataTable> dataTable)
        {
            return Serialize(dataTable);
        }
        
        public Dictionary<Type, DataTable> Deserialize(object serializedData)
        {
            return Deserialize(serializedData as string);
        }

        public string Serialize(Dictionary<Type, DataTable> dataTable)
        {
#if ODIN_INSPECTOR
            byte[] bytesSerializados = SerializationUtility.SerializeValue(dataTable, DataFormat.JSON);
            return Encoding.UTF8.GetString(bytesSerializados);
#else
            return null;
#endif
        }

        public Dictionary<Type, DataTable> Deserialize(string serializedData)
        {
#if ODIN_INSPECTOR
            if (string.IsNullOrEmpty(serializedData)) return new Dictionary<Type, DataTable>();
            byte[] bytesDesserializados = Encoding.UTF8.GetBytes(serializedData);
            return SerializationUtility.DeserializeValue<Dictionary<Type, DataTable>>(bytesDesserializados, 
                DataFormat.JSON);
#else
            return null;
#endif
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

namespace LegendaryTools.Persistence
{
    [CreateAssetMenu(menuName = "Tools/Persistence/XmlProvider", fileName = "XmlProvider", order = 0)]
    public class XmlProvider : ScriptableObject, IStringSerializationProvider
    {
        public string Extension => "xml";
        object ISerializationProvider.Serialize(Dictionary<Type, DataTable> dataTable)
        {
            return Serialize(dataTable);
        }
        
        public Dictionary<Type, DataTable> Deserialize(object serializedData)
        {
            return Deserialize(serializedData as string);
        }

        public Dictionary<Type, DataTable> Deserialize(string serializedData)
        {
            using (StringReader reader = new StringReader(serializedData))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(Dictionary<Type, DataTable>));
                return (Dictionary<Type, DataTable>)serializer.Deserialize(reader);
            }
        }

        public string Serialize(Dictionary<Type, DataTable> dataTable)
        {
            using (StringWriter writer = new StringWriter())
            {
                XmlSerializer serializer = new XmlSerializer(typeof(Dictionary<Type, DataTable>));
                serializer.Serialize(writer, dataTable);
                return writer.ToString();
            }
        }
    }
}
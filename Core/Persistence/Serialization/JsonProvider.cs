using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace LegendaryTools.Persistence
{
    [CreateAssetMenu(menuName = "Tools/Persistence/JsonProvider", fileName = "JsonProvider", order = 0)]
    public class JsonProvider : ScriptableObject, IStringSerializationProvider
    {
        public Formatting FormatType;
        public TypeNameHandling TypeNameHandling = TypeNameHandling.None;
        public PreserveReferencesHandling PreserveReferencesHandling = PreserveReferencesHandling.None;
        public List<JsonConverterProvider> JsonConverterProviders = new List<JsonConverterProvider>();
        public string Extension => "json";

        private readonly JsonSerializerSettings settings = new()
        {
            TypeNameHandling = TypeNameHandling.None,
            PreserveReferencesHandling = PreserveReferencesHandling.None,
            Converters = new List<JsonConverter>
            {
                new Vector3Converter(),
                new Vector2Converter(),
                new QuaternionConverter(),
                new ColorConverter(),
                new Color32Converter(),
                new Vector3IntConverter(),
                new Vector2IntConverter(),
                new RectConverter(),
                new Vector4Converter(),
                new BoundsConverter(),
                new LayerMaskConverter(),
                new AnimationCurveConverter(),
                new GradientConverter(),
                new RectOffsetConverter(),
                new Matrix4x4Converter(),
                new RangeIntConverter(),
                new RectIntConverter(),
                new BoundsIntConverter(),
                new Hash128Converter(),
            }
        };

        private bool isInitialized;
        
        protected virtual void Initialize()
        {
            if (isInitialized) return;
            foreach (JsonConverterProvider jsonConverterProvider in JsonConverterProviders)
            {
                settings.Converters.Add(jsonConverterProvider.JsonConverter);
            }
            settings.TypeNameHandling = TypeNameHandling;
            settings.PreserveReferencesHandling = PreserveReferencesHandling;
            isInitialized = true;
        }
        
        public virtual string Serialize(Dictionary<Type, DataTable> dataTable)
        {
            Initialize();
            return JsonConvert.SerializeObject(dataTable, FormatType, settings);
        }

        public virtual Dictionary<Type, DataTable> Deserialize(string serializedData)
        {
            Initialize();
            if (string.IsNullOrEmpty(serializedData)) return new Dictionary<Type, DataTable>();
            return JsonConvert.DeserializeObject<Dictionary<Type, DataTable>>(serializedData, settings);
        }

        object ISerializationProvider.Serialize(Dictionary<Type, DataTable> dataTable)
        {
            return Serialize(dataTable);
        }

        public Dictionary<Type, DataTable> Deserialize(object serializedData)
        {
            return Deserialize(serializedData as string);
        }
    }
}
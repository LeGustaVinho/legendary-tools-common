using Newtonsoft.Json;
using UnityEngine;

namespace LegendaryTools.Persistence
{
    public abstract class JsonConverterProvider : ScriptableObject
    {
        public abstract JsonConverter JsonConverter { get; }
    }
}
using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Chronos
{
    [CreateAssetMenu(fileName = "ChronosConfig", menuName = "Tools/Chronos/ChronosConfig")]
    public class ChronosConfig : ScriptableObject
    {
        public float ProviderTimeOut;
        public List<DateTimeProvider> WaterfallProviders = new List<DateTimeProvider>();
        
        public void ClearPersistentData()
        {
            Chronos.ClearPersistentData();
        }
    }
}
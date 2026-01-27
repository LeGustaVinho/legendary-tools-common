using System.Collections.Generic;
using UnityEngine;

namespace LegendaryTools.Chronos
{
    [CreateAssetMenu(fileName = "ChronosConfig", menuName = "Tools/Chronos/ChronosConfig")]
    public class ChronosConfig : ScriptableObject
    {
        public ChronosSecurityPolicy SecurityPolicy;

        [Tooltip(
            "Trusted UTC providers. Order matters (waterfall). In Strict mode, offline progress requires a successful provider response.")]
        public List<DateTimeProvider> WaterfallProviders = new();
    }
}
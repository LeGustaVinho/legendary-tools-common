using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools
{
    public abstract class InternetProviderChecker : ScriptableObject
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.SuffixLabel("seconds")]
        [Sirenix.OdinInspector.MinValue(1)]
#endif
        public int TimeOut = 5;
        public abstract Task<bool> HasInternetConnection();
    }
}
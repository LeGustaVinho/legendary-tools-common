using System;
using UnityEngine;

namespace LegendaryTools.Maestro
{
    [CreateAssetMenu(menuName = "Legendary Tools/Maestro/Configuration/Init Step Listing (V2)")]
    public class InitStepListingConfig : ConfigListing<InitStepConfig>, IDisposable
    {
        public void Dispose()
        {
            foreach (InitStepConfig initStepConfig in Configs)
            {
                initStepConfig.Dispose();
            }
        }
    }
}

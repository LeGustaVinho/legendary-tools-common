using UnityEngine;

namespace LegendaryTools.Actor
{
    public abstract class ConfigurableActor<TConfig> : Actor<ConfigurableActorMonoBehaviour<TConfig>>
        where TConfig : ActorConfig
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public TConfig ActorConfig { get; protected set; }
        
        public override bool Possess(ActorMonoBehaviour target)
        {
            bool result = base.Possess(target);
            if (result && target is ConfigurableActorMonoBehaviour<TConfig> configurableActorMonoBehaviour)
            {
                ActorConfig = configurableActorMonoBehaviour.Config;
            }
            return result;
        }
    }
}
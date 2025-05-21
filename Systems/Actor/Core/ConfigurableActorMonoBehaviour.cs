namespace LegendaryTools.Actor
{
    public abstract class ConfigurableActorMonoBehaviour<T> : ActorMonoBehaviour
        where T : ActorConfig
    {
        public T Config;
    }
}

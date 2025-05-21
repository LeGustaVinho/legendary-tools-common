namespace LegendaryTools.Systems.ScreenFlow
{
    public class EntityArgPair<T>
    {
        public readonly T Entity;
        public readonly System.Object Args;

        public EntityArgPair(T uiEntity, object args)
        {
            Entity = uiEntity;
            Args = args;
        }
    }
}
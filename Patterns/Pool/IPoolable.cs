namespace LegendaryTools
{
    public interface IPoolable
    {
        void OnConstruct();

        void OnCreate();

        void OnRecycle();
    }
}
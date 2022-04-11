namespace LegendaryTools
{
    public abstract class Singleton<T>
        where T : class, new()
    {
        static T _instance;

        public static T Instance
        {
            get
            {
                return _instance = _instance != null ? _instance : new T();
            }
        }
    }
}
namespace LegendaryTools.Persistence
{
    public interface IPersistenceCallback
    {
        void OnBeforeSerialize();
        void OnAfterSerialized();
        void OnBeforeDeserialize();
        void OnAfterDeserialize();
    }
}
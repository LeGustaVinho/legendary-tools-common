using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LegendaryTools.Persistence
{
    public interface IPersistence : IDisposable
    {
        public bool IsBusy { get; }
        string Set<T>(T dataToSave, object id = null, int version = 0, bool autoSave = false);
        T Get<T>(object id, T defaultValue = default);
        (int, int, DateTime) GetMetadata<T>();
        Dictionary<string, T> GetCollection<T>();
        bool Delete<T>(object id, bool autoSave = false);
        bool Contains<T>(object id);
        void Save();
        void Load();
        Task SaveAsync();
        Task LoadAsync();
        void AddListener<T>(Action<IPersistence, PersistenceAction, string, object> callback);
        void RemoveListener<T>(Action<IPersistence, PersistenceAction, string, object> callback);
    }
}
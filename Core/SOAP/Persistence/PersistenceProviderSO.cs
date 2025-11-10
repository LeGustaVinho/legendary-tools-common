using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LegendaryTools.Persistence;
using UnityEngine;

namespace LegendaryTools.SOAP
{
    /// <summary>
    /// Base ScriptableObject to allow Unity serialization of IPersistence providers.
    /// Concrete providers (e.g., PlayerPrefs) should derive from this.
    /// </summary>
    public abstract class PersistenceProviderSO : ScriptableObject, IPersistence
    {
        /// <summary>Indicates if provider is performing an operation.</summary>
        public abstract bool IsBusy { get; }

        /// <summary>Store a value of type T under a key (or derived from id).</summary>
        public abstract string Set<T>(T dataToSave, object id = null, int version = 0, bool autoSave = false);

        /// <summary>Retrieve a value of type T from the provider.</summary>
        public abstract T Get<T>(object id, T defaultValue = default);

        public (int, int, DateTime) GetMetadata<T>()
        {
            return (0, 0, DateTime.MinValue);
        }

        public Dictionary<string, T> GetCollection<T>()
        {
            return new Dictionary<string, T>();
        }

        /// <summary>Delete a value of type T associated with the given id.</summary>
        public abstract bool Delete<T>(object id, bool autoSave = false);

        /// <summary>True if the provider contains a value for the given id.</summary>
        public abstract bool Contains<T>(object id);

        /// <summary>Persist pending changes (if any) to durable storage.</summary>
        public abstract void Save();

        /// <summary>Load storage indices / caches (if needed).</summary>
        public abstract void Load();

        public virtual Task SaveAsync()
        {
            Save();
            return Task.CompletedTask;
        }

        public virtual Task LoadAsync()
        {
            Load();
            return Task.CompletedTask;
        }

        public abstract void AddListener<T>(Action<IPersistence, PersistenceAction, string, object> callback);
        public abstract void RemoveListener<T>(Action<IPersistence, PersistenceAction, string, object> callback);

        public virtual void Dispose()
        {
        }

        /// <summary>
        /// Helper to build a human-friendly, collision-resistant key.
        /// </summary>
        protected static string BuildKey(object id, Type t)
        {
            string prefix = string.IsNullOrWhiteSpace(Application.productName) ? "APP" : Application.productName;
            string typeName = t != null ? t.FullName : "UnknownType";
            string token = id is string s ? s : id?.ToString() ?? "default";
            return $"{prefix}/SOAP/{typeName}/{token}";
        }
    }
}
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Persistence
{
    public class Persistence : IPersistence
    {
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public Dictionary<Type, DataTable> DataTables = new Dictionary<Type, DataTable>();
        public PersistenceSettings Settings;
        
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public bool IsBusy { get; private set; }
            
        public const string EMPTY_ID = "";

        private readonly IStorable storable;
        private readonly ISerializationProvider serializationProvider;
        private readonly IEncryptionProvider encryptionProvider;

        private bool CanEncrypt => Settings.Encryptation && encryptionProvider != null;

        private readonly Dictionary<Type, List<Action<IPersistence, PersistenceAction, string, object>>> listeners =
            new Dictionary<Type, List<Action<IPersistence, PersistenceAction, string, object>>>();

        public Persistence(IStorable storable, ISerializationProvider serializationProvider, 
            IEncryptionProvider encryptionProvider = null, PersistenceSettings settings = new PersistenceSettings())
        {
            this.storable = storable;
            this.serializationProvider = serializationProvider;
            this.encryptionProvider = encryptionProvider;
            Settings = settings;
            Load();
        }

        public string Set<T>(T dataToSave, object id = null, int version = -1, bool autoSave = false)
        {
            DataTable dataTable;
            Type dataType = typeof(T);
            string currentId = id == null ? Guid.NewGuid().ToString() : id.ToString();
            if (DataTables.TryGetValue(dataType, out DataTable table))
            {
                dataTable = table;
            }
            else
            {
                dataTable = new DataTable(dataType, version == -1 ? 0 : version, 0, DateTime.UtcNow);
                DataTables.Add(dataType, dataTable);
            }

            if (version == -1)
            {
                version = dataTable.Version;
            }
            else
            {
                if (version < dataTable.Version)
                {
                    Debug.LogError($"[Persistence:Set({dataToSave.GetType()}, {id}, {version})] Downgrade DataTable version is not supported, current version {dataTable.Version}");
                    return currentId;
                }
            }

            if (dataTable.IdentifiedEntries.ContainsKey(currentId))
            {
                dataTable.IdentifiedEntries[currentId] = dataToSave;
                RaiseEvent<T>(PersistenceAction.Update, currentId, dataToSave);
            }
            else
            {
                dataTable.IdentifiedEntries.Add(currentId, dataToSave);
                RaiseEvent<T>(PersistenceAction.Add, currentId, dataToSave);
            }

            dataTable.Revision++;
            dataTable.Timestamp = DateTime.UtcNow;
            dataTable.Version = version;
            
            if(autoSave) Save();
            return currentId;
        }

        public T Get<T>(object id, T defaultValue = default)
        {
            if (id == null) return defaultValue;
            Type dataType = typeof(T);
            if (DataTables.TryGetValue(dataType, out DataTable dataTable))
            {
                if (dataTable.IdentifiedEntries.TryGetValue(id.ToString(), out object data))
                    return (T)data;
            }

            return defaultValue;
        }

        public (int, int, DateTime) GetMetadata<T>()
        {
            Type dataType = typeof(T);
            if (DataTables.TryGetValue(dataType, out DataTable dataTable))
            {
                return (dataTable.Version, dataTable.Revision, dataTable.Timestamp);
            }

            return (-1, -1, default);
        }

        public Dictionary<string, T> GetCollection<T>()
        {
            Type dataType = typeof(T);
            Dictionary<string, T> data = new Dictionary<string, T>();

            if (DataTables.TryGetValue(dataType, out DataTable dataTable))
            {
                foreach (KeyValuePair<string, object> pair in dataTable.IdentifiedEntries)
                {
                    data.Add(pair.Key, (T)pair.Value);
                }
            }
            
            return data;
        }

        public bool Delete<T>(object id, bool autoSave = false)
        {
            if (id == null) return false;
            Type dataType = typeof(T);
            string currentId = id.ToString();
            if (DataTables.TryGetValue(dataType, out DataTable dataTable))
            {
                if (dataTable.IdentifiedEntries.ContainsKey(currentId))
                {
                    RaiseEvent<T>(PersistenceAction.Delete, currentId, dataTable.IdentifiedEntries[currentId]);
                    dataTable.IdentifiedEntries.Remove(currentId);
                    return true;
                }
            }
            
            if(autoSave)
                Save();

            return false;
        }

        public bool Contains<T>(object id)
        {
            Type dataType = typeof(T);
            if (DataTables.TryGetValue(dataType, out DataTable dataTable))
            {
                return dataTable.IdentifiedEntries.ContainsKey(id.ToString());
            }
            
            return false;
        }

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
        public void Save()
        {
            if (IsBusy) return;
            IsBusy = true;
            switch (serializationProvider)
            {
                case IStringSerializationProvider stringSerializationProvider when storable is IStringStorable stringStorable:
                {
                    OnBeforeSerialize();
                    string stringSerializedData = stringSerializationProvider.Serialize(DataTables);
                    OnAfterSerialized();
                    stringSerializedData = SavePostProcessString(stringSerializedData);
                    stringStorable.Save(stringSerializedData);
                    break;
                }
                case IBinarySerializationProvider binarySerializationProvider when storable is IBinaryStorable binaryStorable:
                {
                    OnBeforeSerialize();
                    byte[] binarySerializationData = binarySerializationProvider.Serialize(DataTables);
                    OnAfterSerialized();
                    binarySerializationData = SavePostProcessBinary(binarySerializationData);
                    binaryStorable.Save(binarySerializationData);
                    break;
                }
            }
            IsBusy = false;
        }

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
        public void Load()
        {
            switch (serializationProvider)
            {
                case IStringSerializationProvider stringSerializationProvider when storable is IStringStorable stringStorable:
                {
                    string deserializedStringData = stringStorable.Load();
                    deserializedStringData = LoadPostProcessString(deserializedStringData);
                    OnBeforeDeserialize();
                    DataTables = stringSerializationProvider.Deserialize(deserializedStringData);
                    OnAfterDeserialize();
                    break;
                }
                case IBinarySerializationProvider binarySerializationProvider when storable is IBinaryStorable binaryStorable:
                {
                    byte[] deserializedBinaryData = binaryStorable.Load();
                    deserializedBinaryData = LoadPostProcessBinary(deserializedBinaryData);
                    OnBeforeDeserialize();
                    DataTables = binarySerializationProvider.Deserialize(deserializedBinaryData);
                    OnAfterDeserialize();
                    break;
                }
            }
        }

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
        public async Task SaveAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            switch (serializationProvider)
            {
                case IStringSerializationProvider stringSerializationProvider when storable is IStringStorable stringStorable:
                {
                    OnBeforeSerialize();
                    string stringSerializedData = stringSerializationProvider.Serialize(DataTables);
                    OnAfterSerialized();
                    stringSerializedData = SavePostProcessString(stringSerializedData);
                    await stringStorable.SaveAsync(stringSerializedData);
                    break;
                }
                case IBinarySerializationProvider binarySerializationProvider when storable is IBinaryStorable binaryStorable:
                {
                    OnBeforeSerialize();
                    byte[] binarySerializationData = binarySerializationProvider.Serialize(DataTables);
                    OnAfterSerialized();
                    binarySerializationData = SavePostProcessBinary(binarySerializationData);
                    await binaryStorable.SaveAsync(binarySerializationData);
                    break;
                }
            }
            IsBusy = false;
        }
        
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.Button]
        [Sirenix.OdinInspector.HideInEditorMode]
#endif
        public async Task LoadAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            switch (serializationProvider)
            {
                case IStringSerializationProvider stringSerializationProvider when storable is IStringStorable stringStorable:
                {
                    string deserializedStringData = await stringStorable.LoadAsync();
                    deserializedStringData = LoadPostProcessString(deserializedStringData);
                    OnBeforeDeserialize();
                    DataTables = stringSerializationProvider.Deserialize(deserializedStringData);
                    OnAfterDeserialize();
                    break;
                }
                case IBinarySerializationProvider binarySerializationProvider when storable is IBinaryStorable binaryStorable:
                {
                    byte[] deserializedBinaryData = await binaryStorable.LoadAsync();
                    deserializedBinaryData = LoadPostProcessBinary(deserializedBinaryData);
                    OnBeforeDeserialize();
                    DataTables = binarySerializationProvider.Deserialize(deserializedBinaryData);
                    OnAfterDeserialize();
                    break;
                }
            }
            IsBusy = false;
        }

        public void AddListener<T>(Action<IPersistence, PersistenceAction, string, object> callback)
        {
            Type dataType = typeof(T);
            if (!listeners.ContainsKey(dataType))
            {
                listeners.Add(dataType, new List<Action<IPersistence, PersistenceAction, string, object>>());
            }
            listeners[dataType].Add(callback);
        }
        
        public void RemoveListener<T>(Action<IPersistence, PersistenceAction, string, object> callback)
        {
            Type dataType = typeof(T);
            if (listeners.TryGetValue(dataType,
                    out List<Action<IPersistence, PersistenceAction, string, object>> callbackCollection))
            {
                callbackCollection.Remove(callback);
            }
        }

        private void RaiseEvent<T>(PersistenceAction action, string id, object data)
        {
            Type dataType = typeof(T);
            if (listeners.TryGetValue(dataType,
                    out List<Action<IPersistence, PersistenceAction, string, object>> callbackCollection))
            {
                foreach (Action<IPersistence, PersistenceAction, string, object> callback in callbackCollection)
                {
                    callback?.Invoke(this, action, id, data);
                }
            }
        }
        
        private byte[] SavePostProcessBinary(byte[] binarySerializationData)
        {
            if (CanEncrypt)
                binarySerializationData = encryptionProvider.Encrypt(binarySerializationData);
            
            if (Settings.Gzip)
                binarySerializationData = GZipUtility.Compress(binarySerializationData);

            return binarySerializationData;
        }
        
        private byte[] LoadPostProcessBinary(byte[] deserializedBinaryData)
        {
            if (Settings.Gzip)
                deserializedBinaryData = GZipUtility.Decompress(deserializedBinaryData);
            
            if (CanEncrypt)
                deserializedBinaryData = encryptionProvider.Decrypt(deserializedBinaryData);

            return deserializedBinaryData;
        }

        private string SavePostProcessString(string stringSerializedData)
        {
            byte[] stringSerializedDataBytes = Array.Empty<byte>();
            
            if(CanEncrypt || Settings.Gzip)
                stringSerializedDataBytes = Encoding.UTF8.GetBytes(stringSerializedData);
            
            if (CanEncrypt)
                stringSerializedDataBytes = encryptionProvider.Encrypt(stringSerializedDataBytes);
            
            if (Settings.Gzip)
                stringSerializedDataBytes = GZipUtility.Compress(stringSerializedDataBytes);
            
            if(CanEncrypt || Settings.Gzip)
                stringSerializedData = Base64Utility.BytesToBase64(stringSerializedDataBytes);

            return stringSerializedData;
        }
        
        private string LoadPostProcessString(string deserializedStringData)
        {
            byte[] stringDeserializedDataBytes = Array.Empty<byte>();
            
            if (CanEncrypt || Settings.Gzip)
                stringDeserializedDataBytes = Base64Utility.Base64ToBytes(deserializedStringData);
            
            if (Settings.Gzip)
                stringDeserializedDataBytes = GZipUtility.Decompress(stringDeserializedDataBytes);

            if (CanEncrypt)
                stringDeserializedDataBytes = encryptionProvider.Decrypt(stringDeserializedDataBytes);
            
            if (CanEncrypt || Settings.Gzip)
                deserializedStringData = Encoding.UTF8.GetString(stringDeserializedDataBytes);

            return deserializedStringData;
        }

        public void OnBeforeSerialize()
        {
            foreach (KeyValuePair<Type, DataTable> pairTable in DataTables)
            {
                foreach (KeyValuePair<string, object> pairData in pairTable.Value.IdentifiedEntries)
                {
                    if (pairData.Value is IPersistenceCallback persistenceCallback)
                        persistenceCallback.OnBeforeSerialize();
                }
            }
        }

        public void OnAfterSerialized()
        {
            foreach (KeyValuePair<Type, DataTable> pairTable in DataTables)
            {
                foreach (KeyValuePair<string, object> pairData in pairTable.Value.IdentifiedEntries)
                {
                    if (pairData.Value is IPersistenceCallback persistenceCallback)
                        persistenceCallback.OnAfterSerialized();
                }
            }
        }

        public void OnBeforeDeserialize()
        {
            foreach (KeyValuePair<Type, DataTable> pairTable in DataTables)
            {
                foreach (KeyValuePair<string, object> pairData in pairTable.Value.IdentifiedEntries)
                {
                    if (pairData.Value is IPersistenceCallback persistenceCallback)
                        persistenceCallback.OnBeforeDeserialize();
                }
            }
        }

        public void OnAfterDeserialize()
        {
            foreach (KeyValuePair<Type, DataTable> pairTable in DataTables)
            {
                foreach (KeyValuePair<string, object> pairData in pairTable.Value.IdentifiedEntries)
                {
                    if (pairData.Value is IPersistenceCallback persistenceCallback)
                        persistenceCallback.OnAfterDeserialize();
                }
            }
        }

        public void Dispose()
        {
            DataTables.Clear();
        }
    }
}
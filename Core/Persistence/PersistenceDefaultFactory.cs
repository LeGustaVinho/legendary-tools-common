using System;
using UnityEngine;

namespace LegendaryTools.Persistence
{
    public static class PersistenceDefaultFactory
    {
        private const string DefaultFileName = "save";
        private const string DefaultPostRootPath = "LegendaryTools";
        private const string DefaultKeyStringPrefsKey = "LegendaryTools.Persistence.DefaultKeyString";

        public static void CreateDefault(
            out IStorable storable,
            out ISerializationProvider serializationProvider,
            out IEncryptionProvider encryptionProvider,
            out PersistenceSettings settings)
        {
            settings = new PersistenceSettings
            {
                Gzip = false,
                Encryptation = false
            };

            JsonProvider jsonProvider = ScriptableObject.CreateInstance<JsonProvider>();
            jsonProvider.hideFlags = HideFlags.HideAndDontSave;
            serializationProvider = jsonProvider;

            DiskStorage diskStorage = ScriptableObject.CreateInstance<DiskStorage>();
            diskStorage.hideFlags = HideFlags.HideAndDontSave;

            // Configure a deterministic default path using UnityFilePath (no reflection).
            diskStorage.FilePath = new UnityFilePath
            {
                UseBackwardsSlash = false,
                RootPathType = UnityFilePathType.Persistent,
                PostRootPath = DefaultPostRootPath,
                FileName = DefaultFileName,
                Extension = serializationProvider.Extension
            };

            storable = diskStorage;

            encryptionProvider = null;
            if (settings.Encryptation)
            {
                AesEncryptionProvider aes = ScriptableObject.CreateInstance<AesEncryptionProvider>();
                aes.hideFlags = HideFlags.HideAndDontSave;
                aes.KeyString = GetOrCreateDefaultKeyString();
                encryptionProvider = aes;
            }
        }

        private static string GetOrCreateDefaultKeyString()
        {
            if (PlayerPrefs.HasKey(DefaultKeyStringPrefsKey))
                return PlayerPrefs.GetString(DefaultKeyStringPrefsKey, string.Empty);

            string keyString = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(DefaultKeyStringPrefsKey, keyString);
            PlayerPrefs.Save();
            return keyString;
        }
    }
}
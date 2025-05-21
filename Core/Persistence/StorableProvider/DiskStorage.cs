using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendaryTools.Persistence
{
    [CreateAssetMenu(menuName = "Tools/Persistence/DiskStorage", fileName = "DiskStorage", order = 0)]
    public class DiskStorage : ScriptableObject, IStringStorable, IBinaryStorable
    {
        public UnityFilePath FilePath;
        
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public string StoragePath
        {
            get => FilePath.Path;
        }

        public void Save(string objToSave)
        {
            File.WriteAllText(StoragePath, objToSave);
        }

        public void Save(byte[] objToSave)
        {
            File.WriteAllBytes(StoragePath, objToSave);
        }

        byte[] IBinaryStorable.Load()
        {
            return File.Exists(StoragePath) ? File.ReadAllBytes(StoragePath) : Array.Empty<byte>();
        }

        string IStringStorable.Load()
        {
            return File.Exists(StoragePath) ? File.ReadAllText(StoragePath) : string.Empty;
        }

        public async Task SaveAsync(string objToSave)
        {
            await File.WriteAllTextAsync(StoragePath, objToSave);
        }

        async Task<string> IStringStorable.LoadAsync()
        {
            return File.Exists(StoragePath) ? await File.ReadAllTextAsync(StoragePath) : string.Empty;
        }

        public async Task SaveAsync(byte[] objToSave)
        {
            await File.WriteAllBytesAsync(StoragePath, objToSave);
        }

        async Task<byte[]> IBinaryStorable.LoadAsync()
        {
            return File.Exists(StoragePath) ? await File.ReadAllBytesAsync(StoragePath): Array.Empty<byte>();
        }
    }
}
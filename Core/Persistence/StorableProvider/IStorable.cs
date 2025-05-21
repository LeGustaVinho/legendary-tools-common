using System.Threading.Tasks;

namespace LegendaryTools.Persistence
{
    public interface IStorable
    {
        public string StoragePath { get; }
    }
    
    public interface IStringStorable : IStorable
    {
        public void Save(string objToSave);
        public string Load();
        public Task SaveAsync(string objToSave);
        public Task<string> LoadAsync();
    }
    
    public interface IBinaryStorable : IStorable
    {
        public void Save(byte[] objToSave);
        public byte[] Load();
        public Task SaveAsync(byte[] objToSave);
        public Task<byte[]> LoadAsync();
    }
}
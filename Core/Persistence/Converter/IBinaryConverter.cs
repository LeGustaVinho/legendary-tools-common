using System.IO;

namespace LegendaryTools.Persistence
{
    public interface IBinaryConverter
    {
        public void Write(BinaryWriter writer, object value);
        public object Read(BinaryReader reader);
    }
    
    public interface IBinaryConverter<T> : IBinaryConverter
    {
        public void Write(BinaryWriter writer, T value);
        public new T Read(BinaryReader reader);
    }
}
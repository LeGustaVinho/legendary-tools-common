namespace LegendaryTools.Persistence
{
    public interface IEncryptionProvider
    {
        string KeyString { get; }
        void Initialize();
        byte[] Encrypt(byte[] data);
        byte[] Decrypt(byte[] data);
    }
}
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LegendaryTools.Persistence
{
    public static class GZipUtility
    {
        public static byte[] Compress(byte[] data)
        {
            using (MemoryStream compressedStream = new())
            {
                using (GZipStream gzipStream = new(compressedStream, CompressionMode.Compress))
                {
                    gzipStream.Write(data, 0, data.Length);
                }

                return compressedStream.ToArray();
            }
        }

        public static byte[] Decompress(byte[] compressedData)
        {
            using (MemoryStream compressedStream = new(compressedData))
            {
                using (GZipStream gzipStream = new(compressedStream, CompressionMode.Decompress))
                {
                    using (MemoryStream resultStream = new())
                    {
                        gzipStream.CopyTo(resultStream);
                        return resultStream.ToArray();
                    }
                }
            }
        }

        public static byte[] CompressString(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            return Compress(data);
        }

        public static string DecompressString(byte[] compressedData)
        {
            byte[] decompressedData = Decompress(compressedData);
            return Encoding.UTF8.GetString(decompressedData);
        }
    }
}
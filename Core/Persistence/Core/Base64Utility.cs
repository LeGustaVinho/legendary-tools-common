using System;

namespace LegendaryTools.Persistence
{
    public static class Base64Utility
    {
        public static string BytesToBase64(byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }
        
        public static byte[] Base64ToBytes(string base64String)
        {
            return Convert.FromBase64String(base64String);
        }
    }
}
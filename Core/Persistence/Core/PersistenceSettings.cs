using System;

namespace LegendaryTools.Persistence
{
    [Serializable]
    public struct PersistenceSettings
    {
        public bool Gzip;
        public bool Encryptation;
    }
}
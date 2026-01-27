using System;

namespace LegendaryTools.Chronos
{
    /// <summary>
    /// DTO persisted via IPersistence to store the HMAC secret.
    /// </summary>
    [Serializable]
    public struct ChronosSecretData
    {
        public string SecretBase64;
    }
}
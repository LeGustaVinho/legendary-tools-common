using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace LegendaryTools.Persistence
{
    [CreateAssetMenu(menuName = "Tools/Persistence/AESEncryptionProvider", fileName = "AESEncryptionProvider",
        order = 0)]
    public class AesEncryptionProvider : ScriptableObject, IEncryptionProvider
    {
#if ODIN_INSPECTOR
        [HideInInspector]
#endif
        [SerializeField] private string keyString;

#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowInInspector]
#endif
        public string KeyString
        {
            get => keyString;
            set => keyString = value;
        }

        // Número de iterações para o PBKDF2
        private const int Iterations = 10000;

        // Tamanho do salt em bytes
        private const int SaltSize = 16;

        // Tamanho do IV (Initialization Vector) em bytes
        private const int IvSize = 16;

        private const string SALT = "54727291-2701-4889-858f-f1ef8ed2e774";
        private const string KEY = "d4d76009-34f9-4f3e-b63f-06d5eb25c9f6";
        private const string IV = "41d6aaec-5abf-4ca4-9672-d93b9a02bece";

        private byte[] Salt
        {
            set => PlayerPrefs.SetString(SALT, Base64Utility.BytesToBase64(value));
            get => Base64Utility.Base64ToBytes(PlayerPrefs.GetString(SALT, string.Empty));
        }
        
        private byte[] Key
        {
            set => PlayerPrefs.SetString(KEY, Base64Utility.BytesToBase64(value));
            get => Base64Utility.Base64ToBytes(PlayerPrefs.GetString(KEY, string.Empty));
        }
        
        private byte[] Iv
        {
            set => PlayerPrefs.SetString(IV, Base64Utility.BytesToBase64(value));
            get => Base64Utility.Base64ToBytes(PlayerPrefs.GetString(IV, string.Empty));
        }

        public void Initialize()
        {
            if(!PlayerPrefs.HasKey(SALT))
                // Gerar um salt aleatório
                Salt = GenerateRandomBytes(SaltSize);

            if(!PlayerPrefs.HasKey(KEY))
                // Gerar a chave a partir do KeyString e do salt
                Key = DeriveKey(KeyString, Salt);

            if(!PlayerPrefs.HasKey(IV))
                // Gerar um IV aleatório
                Iv = GenerateRandomBytes(IvSize);
        }

        public byte[] Encrypt(byte[] data)
        {
            if (data == null || data.Length == 0) return Array.Empty<byte>();

            Initialize();
            
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.KeySize = 256;
                aesAlg.Key = Key;
                aesAlg.IV = Iv;
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msEncrypt = new())
                {
                    // Preencher o MemoryStream com o salt e o IV para uso na descriptação
                    msEncrypt.Write(Salt, 0, Salt.Length);
                    msEncrypt.Write(Iv, 0, Iv.Length);

                    using (CryptoStream csEncrypt = new(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        csEncrypt.Write(data, 0, data.Length);
                        csEncrypt.FlushFinalBlock();

                        return msEncrypt.ToArray();
                    }
                }
            }
        }

        public byte[] Decrypt(byte[] data)
        {
            if (data == null || data.Length == 0) return Array.Empty<byte>();
            
            Initialize();

            // Extrair o salt e o IV dos dados
            byte[] salt = new byte[SaltSize];
            byte[] iv = new byte[IvSize];

            Array.Copy(data, 0, salt, 0, SaltSize);
            Array.Copy(data, SaltSize, iv, 0, IvSize);

            // O restante são os dados criptografados
            int cipherTextStartIndex = SaltSize + IvSize;
            int cipherTextLength = data.Length - cipherTextStartIndex;

            byte[] cipherText = new byte[cipherTextLength];
            Array.Copy(data, cipherTextStartIndex, cipherText, 0, cipherTextLength);

            // Gerar a chave a partir do KeyString e do salt extraído
            byte[] key = DeriveKey(KeyString, salt);

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.KeySize = 256;
                aesAlg.Key = key;
                aesAlg.IV = iv;
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msDecrypt = new(cipherText))
                {
                    using (CryptoStream csDecrypt = new(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (MemoryStream msPlain = new())
                        {
                            csDecrypt.CopyTo(msPlain);
                            return msPlain.ToArray();
                        }
                    }
                }
            }
        }

        // Método para gerar uma chave a partir do KeyString e do salt
        private byte[] DeriveKey(string keyString, byte[] salt)
        {
            using (Rfc2898DeriveBytes keyDerivationFunction =
                   new Rfc2898DeriveBytes(keyString, salt, Iterations, HashAlgorithmName.SHA256))
            {
                return keyDerivationFunction.GetBytes(32); // 32 bytes para chave de 256 bits
            }
        }

        // Método para gerar bytes aleatórios para salt e IV
        private byte[] GenerateRandomBytes(int size)
        {
            byte[] bytes = new byte[size];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return bytes;
        }
    }
}
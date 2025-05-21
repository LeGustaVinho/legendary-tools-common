using UnityEngine;
using System;
using System.Security.Cryptography;
using System.IO;
using System.Text;

namespace LegendaryTools.Security
{
    public class Security
    {
        private static RSACryptoServiceProvider RSA = new RSACryptoServiceProvider();
        private static MD5CryptoServiceProvider MD5Hasher = new MD5CryptoServiceProvider();
        private static SHA1CryptoServiceProvider SHA1Hasher = new SHA1CryptoServiceProvider();

        public static bool HasKeyLoaded = false;
        public static bool HasPublicKeyOnly
        {
            get { return RSA.PublicOnly; }
        }
        public static int KeySize
        {
            get { return RSA.KeySize; }
            set
            {
                RSA = new RSACryptoServiceProvider(value);
                HasKeyLoaded = false;
            }
        }

        /*
         * Remember:
         * 
         * Public Key only Encrypt.
         * Private Key Encrypt/Decrypt
         * Common Use: Client -------> Server
         * 
         * Public Key VerifySign
         * Private Key Sign and VerifySign
         * 
         * */

        #region Assymetric

        public static void Import(RSAParameters key)
        {
            RSA.ImportParameters(key);

            HasKeyLoaded = true;
        }

        public static void Import(string xml)
        {
            RSAParameters key = Serialization.LoadXML<RSAParameters>(xml);
            RSA.ImportParameters(key);

            HasKeyLoaded = true;
        }

#if !UNITY_WEBPLAYER

        public static void ImportXmlKey(string path)
        {
            RSAParameters key = Serialization.LoadXMLFromFile<RSAParameters>(path);
            RSA.ImportParameters(key);

            HasKeyLoaded = true;
        }

        public static void ExportPublicKeyToXmlFile(string path)
        {
            RSAParameters publicKey = RSA.ExportParameters(false);
            Serialization.SaveXMLToFile<RSAParameters>(publicKey, path);
        }

        public static void ExportPrivateKeyToXmlFile(string path)
        {
            RSAParameters privateKey = RSA.ExportParameters(true);
            Serialization.SaveXMLToFile<RSAParameters>(privateKey, path);
        }

#endif

        public static RSAParameters ExportKey(bool includePrivateParameters)
        {
            return RSA.ExportParameters(includePrivateParameters);
        }

        public static string ExportKeyString(bool includePrivateParameters)
        {
            return Serialization.SaveXML<RSAParameters>(RSA.ExportParameters(includePrivateParameters));
        }

        public static string GenerateKey(string containerName, bool exportPrivate = false)
        {
            CspParameters cspParameters = new CspParameters();
            cspParameters.KeyContainerName = containerName;
            RSA = new RSACryptoServiceProvider(cspParameters);

            RSAParameters privateKey = RSA.ExportParameters(exportPrivate);
            return Serialization.SaveXML<RSAParameters>(privateKey);
        }

        #region Assymetric Encrypt/Decrypt

#if !UNITY_WEBPLAYER

        public static byte[] EncryptBinaryFile(string path, bool overwriteFileWithEncrypt = false)
        {
            byte[] fileData = File.ReadAllBytes(path);
            byte[] encryptedFileData = RSAEncrypt(fileData);

            if (overwriteFileWithEncrypt)
                File.WriteAllBytes(path, encryptedFileData);

            return encryptedFileData;
        }

        public static byte[] DecryptBinaryFile(string path, bool overwriteFileWithDecrypt = false)
        {
            byte[] fileData = File.ReadAllBytes(path);
            byte[] decryptedFileData = RSADecrypt(fileData);

            if (overwriteFileWithDecrypt)
                File.WriteAllBytes(path, decryptedFileData);

            return decryptedFileData;
        }

        //---------------------------------

        public static string EncryptTextFile(string path, bool overwriteFileWithEncrypt = false)
        {
            string fileData = File.ReadAllText(path);
            string encryptedFileData = EncryptString(fileData);

            if (overwriteFileWithEncrypt)
                File.WriteAllText(path, encryptedFileData);

            return encryptedFileData;
        }

        public static string DecryptTextFile(string path, bool overwriteFileWithDecrypt = false)
        {
            string fileData = File.ReadAllText(path);
            string decryptedFileData = DecryptString(fileData);

            if (overwriteFileWithDecrypt)
                File.WriteAllText(path, decryptedFileData);

            return decryptedFileData;
        }

#endif

        //---------------------------------

        public static byte[] RSAEncrypt(byte[] data, bool fOAEP = false)
        {
            return RSA.Encrypt(data, fOAEP);
        }

        public static byte[] RSADecrypt(byte[] dataEncrypted, bool fOAEP = false)
        {
            return RSA.Decrypt(dataEncrypted, fOAEP);
        }

        //---------------------------------

        public static string EncryptByte(byte[] data)
        {
            byte[] encryptedData = RSAEncrypt(data);
            string base64Data = Convert.ToBase64String(encryptedData);
            return base64Data;
        }

        public static byte[] DecryptByte(string base64Data)
        {
            byte[] encryptedDataBytes = Convert.FromBase64String(base64Data);
            byte[] dencryptedDataBytes = RSADecrypt(encryptedDataBytes);
            return dencryptedDataBytes;
        }

        //---------------------------------

        public static string EncryptChar(char data)
        {
            byte[] dataBytes = BitConverter.GetBytes(data);
            return EncryptByte(dataBytes);
        }

        public static char DecryptChar(string base64Data)
        {
            byte[] dencryptedDataBytes = DecryptByte(base64Data);
            char dencryptedData = BitConverter.ToChar(dencryptedDataBytes, 0);
            return dencryptedData;
        }

        //---------------------------------

        public static string EncryptString(string data)
        {
            byte[] dataBytes = System.Text.Encoding.UTF8.GetBytes(data);
            return EncryptByte(dataBytes);
        }

        public static string DecryptString(string base64Data)
        {
            byte[] dencryptedDataBytes = DecryptByte(base64Data);
            string dencryptedData = Encoding.UTF8.GetString(dencryptedDataBytes);
            return dencryptedData;
        }

        //---------------------------------

        public static string EncryptShort(Int16 data)
        {
            byte[] dataBytes = BitConverter.GetBytes(data);
            return EncryptByte(dataBytes);
        }

        public static Int16 DecryptShort(string base64Data)
        {
            byte[] dencryptedDataBytes = DecryptByte(base64Data);
            Int16 dencryptedData = BitConverter.ToInt16(dencryptedDataBytes, 0);
            return dencryptedData;
        }

        //---------------------------------

        public static string EncryptInt(int data)
        {
            byte[] dataBytes = BitConverter.GetBytes(data);
            return EncryptByte(dataBytes);
        }

        public static int DecryptInt(string base64Data)
        {
            byte[] dencryptedDataBytes = DecryptByte(base64Data);
            int dencryptedData = BitConverter.ToInt32(dencryptedDataBytes, 0);
            return dencryptedData;
        }

        //---------------------------------

        public static string EncryptLong(Int64 data)
        {
            byte[] dataBytes = BitConverter.GetBytes(data);
            return EncryptByte(dataBytes);
        }

        public static Int64 DecryptLong(string base64Data)
        {
            byte[] dencryptedDataBytes = DecryptByte(base64Data);
            Int64 dencryptedData = BitConverter.ToInt64(dencryptedDataBytes, 0);
            return dencryptedData;
        }

        //---------------------------------

        public static string EncryptFloat(float data)
        {
            byte[] dataBytes = BitConverter.GetBytes(data);
            return EncryptByte(dataBytes);
        }

        public static float DecryptFloat(string base64Data)
        {
            byte[] dencryptedDataBytes = DecryptByte(base64Data);
            float dencryptedData = BitConverter.ToSingle(dencryptedDataBytes, 0);
            return dencryptedData;
        }

        //---------------------------------

        public static string EncryptDouble(double data)
        {
            byte[] dataBytes = BitConverter.GetBytes(data);
            return EncryptByte(dataBytes);
        }

        public static double DecryptDouble(string base64Data)
        {
            byte[] dencryptedDataBytes = DecryptByte(base64Data);
            double dencryptedData = BitConverter.ToDouble(dencryptedDataBytes, 0);
            return dencryptedData;
        }

        //---------------------------------

        public static string EncryptBool(bool data)
        {
            byte[] dataBytes = BitConverter.GetBytes(data);
            return EncryptByte(dataBytes);
        }

        public static bool DecryptBool(string base64Data)
        {
            byte[] dencryptedDataBytes = DecryptByte(base64Data);
            bool dencryptedData = BitConverter.ToBoolean(dencryptedDataBytes, 0);
            return dencryptedData;
        }

        //---------------------------------

        public static string EncryptVector2(Vector2 data)
        {
            byte[] dataBytes;

            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(data.x);
                    writer.Write(data.y);
                }

                dataBytes = stream.ToArray();
            }

            return EncryptByte(dataBytes);
        }

        public static Vector2 DecryptVector2(string base64Data)
        {
            byte[] dencryptedDataBytes = DecryptByte(base64Data);

            using (MemoryStream stream = new MemoryStream(dencryptedDataBytes))
            {
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    return new Vector2(reader.ReadSingle(), reader.ReadSingle());
                }
            }
        }

        //---------------------------------

        public static string EncryptVector3(Vector3 data)
        {
            byte[] dataBytes;

            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(data.x);
                    writer.Write(data.y);
                    writer.Write(data.z);
                }

                dataBytes = stream.ToArray();
            }

            return EncryptByte(dataBytes);
        }

        public static Vector3 DecryptVector3(string base64Data)
        {
            byte[] dencryptedDataBytes = DecryptByte(base64Data);

            using (MemoryStream stream = new MemoryStream(dencryptedDataBytes))
            {
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
            }
        }

        //---------------------------------

        public static string EncryptQuaternion(Quaternion data)
        {
            byte[] dataBytes;

            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(data.x);
                    writer.Write(data.y);
                    writer.Write(data.z);
                    writer.Write(data.w);
                }

                dataBytes = stream.ToArray();
            }

            return EncryptByte(dataBytes);
        }

        public static Quaternion DecryptQuaternion(string base64Data)
        {
            byte[] dencryptedDataBytes = DecryptByte(base64Data);

            using (MemoryStream stream = new MemoryStream(dencryptedDataBytes))
            {
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
            }
        }

        //---------------------------------

        public static string EncryptGeneric<T>(T data)
        {
            return EncryptByte(Serialization.SaveBinary<T>(data));
        }

        public static T DecryptGeneric<T>(string base64data)
        {
            return Serialization.LoadBinary<T>(DecryptByte(base64data));
        }

        #endregion

        #region Assymetric Data Sign/Verify

        public static byte[] SignDataRaw(byte[] dataToSign)
        {
            try
            {
                // Hash and sign the data. Pass a new instance of SHA1CryptoServiceProvider 
                // to specify the use of SHA1 for hashing. 
                return RSA.SignData(dataToSign, SHA1Hasher);
            }
            catch (CryptographicException e)
            {
                Debug.Log(e.Message);
                return null;
            }
        }

        public static bool VerifyDataRaw(byte[] dataToVerify, byte[] signedData)
        {
            try
            {
                // Verify the data using the signature.  Pass a new instance of SHA1CryptoServiceProvider 
                // to specify the use of SHA1 for hashing. 
                return RSA.VerifyData(dataToVerify, SHA1Hasher, signedData);

            }
            catch (CryptographicException e)
            {
                Debug.Log(e.Message);
                return false;
            }
        }

        //---------------------------------

        public static string SignDataByte(byte[] dataToSign)
        {
            byte[] byteHashSignature = SignDataRaw(dataToSign);
            return ToBase64String(byteHashSignature);
        }

        public static bool VerifyDataByte(byte[] dataToVerify, string hashSignature)
        {
            byte[] byteHashSignature = FromBase64String(hashSignature);
            return VerifyDataRaw(dataToVerify, byteHashSignature);
        }

        //---------------------------------

        public static string SignDataString(string dataToSign)
        {
            byte[] dataBytes = System.Text.Encoding.UTF8.GetBytes(dataToSign);
            return SignDataByte(dataBytes);
        }

        public static bool VerifyDataString(string dataToVerify, string hashSignature)
        {
            byte[] dataBytes = System.Text.Encoding.UTF8.GetBytes(dataToVerify);
            return VerifyDataByte(dataBytes, hashSignature);
        }

        //---------------------------------

#if !UNITY_WEBPLAYER

        public static string SignFile(string path)
        {
            byte[] fileData = File.ReadAllBytes(path);
            return SignDataByte(fileData);
        }

        public static bool VerifyFile(string path, string hashSignature)
        {
            byte[] fileData = File.ReadAllBytes(path);
            return VerifyDataByte(fileData, hashSignature);
        }

#endif

        #endregion

        #endregion

        public static string ToBase64String(byte[] data)
        {
            if (data != null && data.Length > 0)
                return Convert.ToBase64String(data);
            else
                return String.Empty;
        }

        public static byte[] FromBase64String(string base64String)
        {
            if (String.IsNullOrEmpty(base64String))
                return new byte[0];
            else
                return Convert.FromBase64String(base64String);
        }

        public static string Md5FromByte(byte[] dataBytes)
        {
            byte[] BytePass = MD5Hasher.ComputeHash(dataBytes);

            StringBuilder PlainText = new StringBuilder();

            foreach (byte bytePass in BytePass)
                PlainText.Append(bytePass.ToString("x2").ToLower());

            return PlainText.ToString();
        }

        public static string Md5(string pass)
        {
            byte[] BytePass = Encoding.UTF8.GetBytes(pass);

            return Md5FromByte(BytePass);
        }

        public static string Sha1FromByte(byte[] dataBytes)
        {
            byte[] BytePass = new SHA1CryptoServiceProvider().ComputeHash(dataBytes);

            StringBuilder PlainText = new StringBuilder();

            foreach (byte bytePass in BytePass)
                PlainText.Append(bytePass.ToString("x2").ToLower());

            return PlainText.ToString();
        }

        public static string Sha1(string pass)
        {
            byte[] BytePass = Encoding.UTF8.GetBytes(pass);

            return Sha1FromByte(BytePass);
        }
    }
}
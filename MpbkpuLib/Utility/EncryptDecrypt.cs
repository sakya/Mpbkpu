using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace MpbkpuLib.Utility
{
    /// <summary>
    /// // https://tomrucki.com/posts/aes-encryption-in-csharp/
    /// </summary>
    internal class EncryptDecrypt
    {        
        static byte[]? _KeyBytes;

        public EncryptDecrypt(string? key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            using (HashAlgorithm hash = MD5.Create()) {
                _KeyBytes = hash.ComputeHash(Encoding.Unicode.GetBytes(key));
            }
        }

        public async Task<bool> Encrypt(Stream source, Stream destination)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (_KeyBytes == null)
                throw new Exception("Cannot encrypt without a key");

            using (var aesAlg = Aes.Create()) {
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;
                using (var encryptor = aesAlg.CreateEncryptor(_KeyBytes, aesAlg.IV)) {
                    using (var csEncrypt = new CryptoStream(destination, encryptor, CryptoStreamMode.Write)) {
                        byte[] buffer = new byte[4096];

                        await csEncrypt.WriteAsync(aesAlg.IV);
                        while (await source.ReadAsync(buffer) > 0) {
                            await csEncrypt.WriteAsync(buffer);
                        }
                    }
                }
            }

            return true;
        } // Encrypt

        public async Task<bool> Decrypt(Stream source, Stream destination)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (_KeyBytes == null)
                throw new Exception("Cannot decrypt without a key");

            var iv = new byte[16];
            if (await source.ReadAsync(iv, 0, iv.Length) != 16)
                throw new Exception("Cannot read IV");

            using (var aesAlg = Aes.Create()) {
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;
                using (var decryptor = aesAlg.CreateDecryptor(_KeyBytes, iv)) {
                    using (var csDecrypt = new CryptoStream(source, decryptor, CryptoStreamMode.Read)) {
                        byte[] buffer = new byte[4096];
                        while (await csDecrypt.ReadAsync(buffer) > 0) {
                            await destination.WriteAsync(buffer);
                        }
                    }
                }
            }

            return true;
        } // Decrypt
    }
}

using System.Text;
using System.Security.Cryptography;

namespace MpbkpuLib.Utility
{
    /// <summary>
    /// // https://tomrucki.com/posts/aes-encryption-in-csharp/
    /// </summary>
    internal class EncryptDecrypt
    {        
        static byte[]? _keyBytes;

        public EncryptDecrypt(string? key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            using (HashAlgorithm hash = MD5.Create()) {
                _keyBytes = hash.ComputeHash(Encoding.Unicode.GetBytes(key));
            }
        }

        public async Task<bool> Encrypt(Stream source, Stream destination, Action<long>? progressAction = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (_keyBytes == null)
                throw new Exception("Cannot encrypt without a key");

            using (var aesAlg = Aes.Create()) {
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;
                using (var encryptor = aesAlg.CreateEncryptor(_keyBytes, aesAlg.IV)) {
                    using (var csEncrypt = new CryptoStream(destination, encryptor, CryptoStreamMode.Write)) {
                        byte[] buffer = new byte[4096];

                        await csEncrypt.WriteAsync(aesAlg.IV);
                        long processed = 0;
                        int read;
                        while ((read = await source.ReadAsync(buffer)) > 0) {
                            await csEncrypt.WriteAsync(buffer);
                            
                            processed += read;
                            progressAction?.Invoke(processed);
                        }
                    }
                }
            }

            return true;
        } // Encrypt

        public async Task<bool> Decrypt(Stream source, Stream destination, Action<long>? progressAction = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (_keyBytes == null)
                throw new Exception("Cannot decrypt without a key");

            var iv = new byte[16];
            if (await source.ReadAsync(iv, 0, iv.Length) != 16)
                throw new Exception("Cannot read IV");

            using (var aesAlg = Aes.Create()) {
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;
                using (var decryptor = aesAlg.CreateDecryptor(_keyBytes, iv)) {
                    using (var csDecrypt = new CryptoStream(source, decryptor, CryptoStreamMode.Read)) {
                        byte[] buffer = new byte[4096];
                        
                        long processed = 0;
                        int read;                        
                        while ((read = await csDecrypt.ReadAsync(buffer)) > 0) {
                            await destination.WriteAsync(buffer);
                            
                            processed += read;
                            progressAction?.Invoke(processed);                            
                        }
                    }
                }
            }

            return true;
        } // Decrypt
    }
}

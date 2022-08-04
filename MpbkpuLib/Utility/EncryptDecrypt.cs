using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;
using MpbkpuLib.DataModel;

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

        /// <summary>
        /// Encrypt a stream
        /// </summary>
        /// <param name="source">The source <see cref="Stream"/></param>
        /// <param name="destination">The destination <see cref="Stream"/></param>
        /// <param name="compress">If set to true the <see cref="source"/> is compressed before the encryption</param>
        /// <param name="progressAction">A <see cref="Action{T}"/> called to report progress</param>
        /// <returns>True on success</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<bool> Encrypt(BackupItem backupItem, Stream source, Stream destination, bool compress, Action<long>? progressAction = null)
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
                using (var hasher = MD5.Create()) {
                    using (var encryptor = aesAlg.CreateEncryptor(_keyBytes, aesAlg.IV)) {
                        using (CryptoStream hashStream = new CryptoStream(destination, hasher, CryptoStreamMode.Write, true)) {
                            using (var csEncrypt = new CryptoStream(hashStream, encryptor, CryptoStreamMode.Write)) {
                                byte[] buffer = new byte[4096];

                                await csEncrypt.WriteAsync(aesAlg.IV);
                                long processed = 0;
                                int read;
                                if (compress) {
                                    using (var gzipStream = new GZipStream(csEncrypt, CompressionLevel.Optimal, true)) {
                                        while ((read = await source.ReadAsync(buffer)) > 0) {
                                            await gzipStream.WriteAsync(buffer);

                                            processed += read;
                                            progressAction?.Invoke(processed);
                                        }
                                    }
                                } else {
                                    while ((read = await source.ReadAsync(buffer)) > 0) {
                                        await csEncrypt.WriteAsync(buffer);

                                        processed += read;
                                        progressAction?.Invoke(processed);
                                    }
                                }
                            }
                        }
                    }

                    backupItem.Size = destination.Position;;
                    backupItem.MD5 = BitConverter.ToString(hasher.Hash!).Replace("-", "").ToLowerInvariant();
                }
            }

            return true;
        } // Encrypt

        /// <summary>
        /// Decrypt a stream
        /// </summary>
        /// <param name="source">The source <see cref="Stream"/></param>
        /// <param name="destination">The destination <see cref="Stream"/></param>
        /// <param name="decompress">If set to true the <see cref="source"/> is decompressed after the encryption</param>
        /// <param name="progressAction">A <see cref="Action{T}"/> called to report progress</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<bool> Decrypt(Stream source, Stream destination, bool decompress, Action<long>? progressAction = null)
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
                        if (decompress) {
                            using (var gzipStream = new GZipStream(destination, CompressionLevel.Optimal, true)) {
                                while ((read = await csDecrypt.ReadAsync(buffer)) > 0) {
                                    await gzipStream.WriteAsync(buffer);

                                    processed += read;
                                    progressAction?.Invoke(processed);
                                }
                            }
                        } else {
                            while ((read = await csDecrypt.ReadAsync(buffer)) > 0) {
                                await destination.WriteAsync(buffer);

                                processed += read;
                                progressAction?.Invoke(processed);
                            }
                        }
                    }
                }
            }

            return true;
        } // Decrypt
    }
}

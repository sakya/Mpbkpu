using System.IO.Compression;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Graph.SecurityNamespace;
using MpbkpuLib.DataModel;
using Aes = System.Security.Cryptography.Aes;

namespace MpbkpuLib.Utility
{
    /// <summary>
    /// // https://tomrucki.com/posts/aes-encryption-in-csharp/
    /// </summary>
    internal class EncryptDecrypt
    {
        public const string Header = "MPBKPU_ENCRYPTED";
        private static byte[]? _keyBytes;

        public EncryptDecrypt(string? key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            using (HashAlgorithm hash = SHA256.Create()) {
                _keyBytes = hash.ComputeHash(Encoding.Unicode.GetBytes(key));
            }
        }

        public async Task<string> Encrypt(string text)
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(text));
            using var dest = new MemoryStream();
            await Encrypt(null, source, dest, false);
            dest.Position = 0;
            return Convert.ToBase64String(dest.ToArray());
        }

        public async Task<string> Decrypt(string text)
        {
            using var source = new MemoryStream(Convert.FromBase64String(text));
            using var dest = new MemoryStream();
            await Decrypt(source, dest, false);
            dest.Position = 0;
            return Encoding.UTF8.GetString(dest.ToArray());
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
        public Task<bool> Encrypt(Stream source, Stream destination, bool compress, Action<long>? progressAction = null)
        {
            return Encrypt(null, source, destination, compress, progressAction);
        }

        /// <summary>
        /// Encrypt a stream
        /// </summary>
        /// <param name="backupItem">The <see cref="BackupItem"/></param>
        /// <param name="source">The source <see cref="Stream"/></param>
        /// <param name="destination">The destination <see cref="Stream"/></param>
        /// <param name="compress">If set to true the <see cref="source"/> is compressed before the encryption</param>
        /// <param name="progressAction">A <see cref="Action{T}"/> called to report progress</param>
        /// <returns>True on success</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<bool> Encrypt(BackupItem? backupItem, Stream source, Stream destination, bool compress, Action<long>? progressAction = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (_keyBytes == null)
                throw new Exception("Cannot encrypt without a key");

            // Header
            byte[] header = Encoding.ASCII.GetBytes(Header);
            await destination.WriteAsync(header);

            using (var aesAlg = Aes.Create()) {
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;
                using (var sourceHasher = SHA256.Create()) {
                    using (var destHasher = SHA256.Create()) {
                        destHasher.TransformBlock(header, 0, header.Length, null, 0);
                        using (var encryptor = aesAlg.CreateEncryptor(_keyBytes, aesAlg.IV)) {
                            using (CryptoStream hashStream = new CryptoStream(destination, destHasher, CryptoStreamMode.Write, true)) {
                                using (var csEncrypt = new CryptoStream(hashStream, encryptor, CryptoStreamMode.Write)) {
                                    byte[] buffer = new byte[32768];

                                    await csEncrypt.WriteAsync(aesAlg.IV);
                                    long processed = 0;
                                    int read;
                                    if (compress) {
                                        using (var gzipStream =
                                               new GZipStream(csEncrypt, CompressionLevel.Optimal, true)) {
                                            while ((read = await source.ReadAsync(buffer)) > 0) {
                                                sourceHasher.TransformBlock(buffer, 0, read, null, 0);
                                                await gzipStream.WriteAsync(buffer, 0, read);

                                                processed += read;
                                                progressAction?.Invoke(processed);
                                            }
                                        }
                                    } else {
                                        while ((read = await source.ReadAsync(buffer)) > 0) {
                                            sourceHasher.TransformBlock(buffer, 0, read, null, 0);
                                            await csEncrypt.WriteAsync(buffer, 0, read);

                                            processed += read;
                                            progressAction?.Invoke(processed);
                                        }
                                    }
                                }
                            }
                        }
                        if (backupItem != null) {
                            backupItem.Size = destination.Position;
                            backupItem.Sha256 = BitConverter.ToString(destHasher.Hash!).Replace("-", "").ToLowerInvariant();
                        }
                    }

                    sourceHasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    if (backupItem != null)
                        backupItem.OriginalSha256 = BitConverter.ToString(sourceHasher.Hash!).Replace("-", "").ToLowerInvariant();
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
        /// <returns>The SHA356 hash of the decrypted data</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<string> Decrypt(Stream source, Stream destination, bool decompress, Action<long>? progressAction = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (_keyBytes == null)
                throw new Exception("Cannot decrypt without a key");

            // Check header
            var header = new byte[Header.Length];
            var hRead = await source.ReadAsync(header);
            if (hRead != header.Length || Encoding.ASCII.GetString(header) != Header)
                throw new Exception("This file is not encrypted");

            var iv = new byte[16];
            if (await source.ReadAsync(iv, 0, iv.Length) != 16)
                throw new Exception("Cannot read IV");

            string? res = null;
            using (var aesAlg = Aes.Create()) {
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;
                using (var hasher = SHA256.Create()) {
                    using (var decrypter = aesAlg.CreateDecryptor(_keyBytes, iv)) {
                        using (var csDecrypt = new CryptoStream(source, decrypter, CryptoStreamMode.Read)) {
                            byte[] buffer = new byte[32768];

                            long processed = 0;
                            int read;
                            if (decompress) {
                                using (var gzipStream = new GZipStream(csDecrypt, CompressionMode.Decompress, true)) {
                                    while ((read = await gzipStream.ReadAsync(buffer)) > 0) {
                                        hasher.TransformBlock(buffer, 0, read, null, 0);
                                        await destination.WriteAsync(buffer, 0, read);

                                        processed += read;
                                        progressAction?.Invoke(processed);
                                    }
                                }
                            } else {
                                while ((read = await csDecrypt.ReadAsync(buffer)) > 0) {
                                    hasher.TransformBlock(buffer, 0, read, null, 0);
                                    await destination.WriteAsync(buffer, 0, read);

                                    processed += read;
                                    progressAction?.Invoke(processed);
                                }
                            }
                        }
                    }

                    hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    res = BitConverter.ToString(hasher.Hash!).Replace("-", "").ToLowerInvariant();
                }
            }

            return res;
        } // Decrypt
    }
}

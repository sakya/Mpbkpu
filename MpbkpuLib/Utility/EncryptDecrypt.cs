using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Security.Cryptography;
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
        public static readonly int HeaderSize = Header.Length;
        private readonly string? _password;

        public const int AesBlockByteSize = 128 / 8;
        public const int PasswordSaltByteSize = 128 / 8;
        private const int PasswordByteSize = 256 / 8;
        private const int PasswordIterationCount = 100000;
        private readonly RandomNumberGenerator _random = RandomNumberGenerator.Create();

        public EncryptDecrypt(string? password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentNullException(nameof(password));

            _password = password;
        }

        public async Task<string> Encrypt(string text)
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(text));
            using var dest = new MemoryStream();
            await Encrypt(null, source, dest, CompressionLevel.NoCompression);
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
        /// <param name="compressionLevel">The <see cref="CompressionLevel"/> to use on <see cref="source"/> before the encryption</param>
        /// <param name="progressAction">A <see cref="Action{T}"/> called to report progress</param>
        /// <returns>True on success</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public Task<bool> Encrypt(Stream source, Stream destination, CompressionLevel compressionLevel, Action<long>? progressAction = null)
        {
            return Encrypt(null, source, destination, compressionLevel, progressAction);
        }

        /// <summary>
        /// Encrypt a stream
        /// </summary>
        /// <param name="backupItem">The <see cref="BackupItem"/></param>
        /// <param name="source">The source <see cref="Stream"/></param>
        /// <param name="destination">The destination <see cref="Stream"/></param>
        /// <param name="compressionLevel">The <see cref="CompressionLevel"/> to use on <see cref="source"/> before the encryption</param>
        /// <param name="progressAction">A <see cref="Action{T}"/> called to report progress</param>
        /// <param name="token">The <see cref="CancellationToken"/></param>
        /// <returns>True on success</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<bool> Encrypt(BackupItem? backupItem, Stream source, Stream destination, CompressionLevel compressionLevel, Action<long>? progressAction = null,
            CancellationToken? token = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (string.IsNullOrEmpty(_password))
                throw new Exception("Cannot encrypt without a password");

            // Header
            byte[] header = Encoding.ASCII.GetBytes(Header);
            await destination.WriteAsync(header);

            using (var aesAlg = CreateAes()) {
                var iv = GenerateRandomBytes(AesBlockByteSize);
                var keySalt = GenerateRandomBytes(PasswordSaltByteSize);
                var key = GetKey(_password, keySalt);

                await destination.WriteAsync(iv);
                await destination.WriteAsync(keySalt);

                using (var sourceHasher = SHA256.Create()) {
                    using (var destHasher = SHA256.Create()) {
                        destHasher.TransformBlock(header, 0, header.Length, null, 0);
                        destHasher.TransformBlock(iv, 0, iv.Length, null, 0);
                        destHasher.TransformBlock(keySalt, 0, iv.Length, null, 0);
                        using (var encryptor = aesAlg.CreateEncryptor(key, iv)) {
                            using (CryptoStream hashStream = new CryptoStream(destination, destHasher, CryptoStreamMode.Write, true)) {
                                using (var csEncrypt = new CryptoStream(hashStream, encryptor, CryptoStreamMode.Write)) {
                                    byte[] buffer = new byte[32768];

                                    long processed = 0;
                                    int read;
                                    if (compressionLevel != CompressionLevel.NoCompression) {
                                        using (var gzipStream =
                                               new GZipStream(csEncrypt, compressionLevel, true)) {
                                            while ((read = await source.ReadAsync(buffer)) > 0) {
                                                token?.ThrowIfCancellationRequested();
                                                sourceHasher.TransformBlock(buffer, 0, read, null, 0);
                                                await gzipStream.WriteAsync(buffer, 0, read);

                                                processed += read;
                                                progressAction?.Invoke(processed);
                                            }
                                        }
                                    } else {
                                        while ((read = await source.ReadAsync(buffer)) > 0) {
                                            token?.ThrowIfCancellationRequested();
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
        /// <param name="token">The <see cref="CancellationToken"/></param>
        /// <returns>The SHA356 hash of the decrypted data</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<string> Decrypt(Stream source, Stream destination, bool decompress, Action<long>? progressAction = null, CancellationToken? token = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (string.IsNullOrEmpty(_password))
                throw new Exception("Cannot encrypt without a password");

            // Check header
            var header = new byte[HeaderSize];
            var hRead = await source.ReadAsync(header);
            if (hRead != header.Length || Encoding.ASCII.GetString(header) != Header)
                throw new Exception("This file is not encrypted");

            var iv = new byte[AesBlockByteSize];
            if (await source.ReadAsync(iv, 0, iv.Length) != AesBlockByteSize)
                throw new Exception("Cannot read IV");
            var keySalt = new byte[PasswordSaltByteSize];
            if (await source.ReadAsync(keySalt, 0, keySalt.Length) != PasswordSaltByteSize)
                throw new Exception("Cannot read key salt");

            string? res;
            using (var aesAlg = CreateAes()) {
                var key = GetKey(_password, keySalt);
                using (var hasher = SHA256.Create()) {
                    using (var decryptor = aesAlg.CreateDecryptor(key, iv)) {
                        using (var csDecrypt = new CryptoStream(source, decryptor, CryptoStreamMode.Read)) {
                            byte[] buffer = new byte[32768];

                            long processed = 0;
                            int read;
                            if (decompress) {
                                using (var gzipStream = new GZipStream(csDecrypt, CompressionMode.Decompress, true)) {
                                    while ((read = await gzipStream.ReadAsync(buffer)) > 0) {
                                        token?.ThrowIfCancellationRequested();
                                        hasher.TransformBlock(buffer, 0, read, null, 0);
                                        await destination.WriteAsync(buffer, 0, read);

                                        processed += read;
                                        progressAction?.Invoke(processed);
                                    }
                                }
                            } else {
                                while ((read = await csDecrypt.ReadAsync(buffer)) > 0) {
                                    token?.ThrowIfCancellationRequested();
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

        private Aes CreateAes()
        {
            var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            return aes;
        }

        private byte[] GenerateRandomBytes(int numberOfBytes)
        {
            var randomBytes = new byte[numberOfBytes];
            _random.GetBytes(randomBytes);
            return randomBytes;
        }

        private byte[] GetKey(string password, byte[] passwordSalt)
        {
            var keyBytes = Encoding.UTF8.GetBytes(password);
            using var derivator = new Rfc2898DeriveBytes(
                keyBytes, passwordSalt,
                PasswordIterationCount, HashAlgorithmName.SHA256);
            return derivator.GetBytes(PasswordByteSize);
        }
    }
}

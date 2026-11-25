using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;
using MpbkpuLib.DataModel;
using Aes = System.Security.Cryptography.Aes;

namespace MpbkpuLib.Utility
{
    /// <summary>
    /// AES-256-CBC encryption with a PBKDF2 derived key.
    /// Format v1 (read only): header | IV | salt | ciphertext
    /// Format v2: header | IV | salt | ciphertext | HMAC-SHA256(header | IV | salt | ciphertext)
    /// </summary>
    internal class EncryptDecrypt
    {
        public const string Header = "MPBKPU_ENCRYPTED";
        public const string HeaderV2 = "MPBKPU_ENCRYPT02";
        public static readonly int HeaderSize = Header.Length;

        public const int AesBlockByteSize = 128 / 8;
        public const int PasswordSaltByteSize = 128 / 8;
        public const int MacByteSize = 256 / 8;
        private const int KeyByteSize = 256 / 8;
        private const int PasswordIterationCount = 100000;

        private class Keys
        {
            public Keys(byte[] salt, byte[] encryptionKey, byte[]? macKey)
            {
                Salt = salt;
                EncryptionKey = encryptionKey;
                MacKey = macKey;
            }

            public byte[] Salt { get; }
            public byte[] EncryptionKey { get; }
            public byte[]? MacKey { get; }
        }

        private readonly string _password;
        // The key derivation is slow on purpose: derive once and reuse the keys (each file has its own random IV)
        private Keys? _encryptionKeys;
        private readonly Dictionary<string, Keys> _decryptionKeys = new();

        public EncryptDecrypt(string? password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentNullException(nameof(password));

            _password = password;
        }

        /// <summary>
        /// Check if a header identifies an encrypted file
        /// </summary>
        public static bool IsEncryptedHeader(string header)
        {
            return header is Header or HeaderV2;
        }

        public async Task<string> Encrypt(string text)
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(text));
            using var dest = new MemoryStream();
            await Encrypt(null, source, dest, CompressionLevel.NoCompression);
            return Convert.ToBase64String(dest.ToArray());
        }

        public async Task<string> Decrypt(string text)
        {
            using var source = new MemoryStream(Convert.FromBase64String(text));
            using var dest = new MemoryStream();
            await Decrypt(source, dest, false);
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
        public Task<bool> Encrypt(Stream source, Stream destination, CompressionLevel compressionLevel, Action<long>? progressAction = null)
        {
            return Encrypt(null, source, destination, compressionLevel, progressAction);
        }

        /// <summary>
        /// Encrypt a stream
        /// </summary>
        /// <param name="backupItem">The <see cref="BackupItem"/>: receives size and hashes</param>
        /// <param name="source">The source <see cref="Stream"/></param>
        /// <param name="destination">The destination <see cref="Stream"/></param>
        /// <param name="compressionLevel">The <see cref="CompressionLevel"/> to use on <see cref="source"/> before the encryption</param>
        /// <param name="progressAction">A <see cref="Action{T}"/> called to report progress</param>
        /// <param name="token">The <see cref="CancellationToken"/></param>
        /// <returns>True on success</returns>
        public async Task<bool> Encrypt(BackupItem? backupItem, Stream source, Stream destination, CompressionLevel compressionLevel, Action<long>? progressAction = null,
            CancellationToken? token = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            _encryptionKeys ??= DeriveKeys(GenerateRandomBytes(PasswordSaltByteSize), true);
            var iv = GenerateRandomBytes(AesBlockByteSize);

            using var sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var destHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _encryptionKeys.MacKey!);
            await using (var output = new HashingWriteStream(destination, destHash, mac)) {
                await output.WriteAsync(Encoding.ASCII.GetBytes(HeaderV2));
                await output.WriteAsync(iv);
                await output.WriteAsync(_encryptionKeys.Salt);

                using var aes = CreateAes();
                using var encryptor = aes.CreateEncryptor(_encryptionKeys.EncryptionKey, iv);
                await using var csEncrypt = new CryptoStream(output, encryptor, CryptoStreamMode.Write, true);
                Stream writer = csEncrypt;
                GZipStream? gzipStream = null;
                if (compressionLevel != CompressionLevel.NoCompression)
                    writer = gzipStream = new GZipStream(csEncrypt, compressionLevel, true);

                var buffer = new byte[32768];
                long processed = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0) {
                    token?.ThrowIfCancellationRequested();
                    sourceHash.AppendData(buffer, 0, read);
                    await writer.WriteAsync(buffer.AsMemory(0, read));

                    processed += read;
                    progressAction?.Invoke(processed);
                }

                if (gzipStream != null)
                    await gzipStream.DisposeAsync();
                await csEncrypt.FlushFinalBlockAsync();
            }

            var macValue = mac.GetHashAndReset();
            destHash.AppendData(macValue);
            await destination.WriteAsync(macValue);

            if (backupItem != null) {
                backupItem.Size = destination.Position;
                backupItem.Sha256 = Convert.ToHexString(destHash.GetHashAndReset()).ToLowerInvariant();
                backupItem.OriginalSha256 = Convert.ToHexString(sourceHash.GetHashAndReset()).ToLowerInvariant();
            }

            return true;
        } // Encrypt

        /// <summary>
        /// Decrypt a stream.
        /// For v2 files the authentication is verified at the end: on failure an exception is thrown,
        /// so <see cref="destination"/> must be discarded by the caller.
        /// </summary>
        /// <param name="source">The source <see cref="Stream"/></param>
        /// <param name="destination">The destination <see cref="Stream"/></param>
        /// <param name="decompress">If set to true the <see cref="source"/> is decompressed after the decryption</param>
        /// <param name="progressAction">A <see cref="Action{T}"/> called to report progress</param>
        /// <param name="token">The <see cref="CancellationToken"/></param>
        /// <returns>The SHA256 hash of the decrypted data</returns>
        public async Task<string> Decrypt(Stream source, Stream destination, bool decompress, Action<long>? progressAction = null, CancellationToken? token = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            var header = new byte[HeaderSize];
            if (await source.ReadAtLeastAsync(header, header.Length, false) != HeaderSize || !IsEncryptedHeader(Encoding.ASCII.GetString(header)))
                throw new Exception("This file is not encrypted");
            var authenticated = Encoding.ASCII.GetString(header) == HeaderV2;

            var iv = new byte[AesBlockByteSize];
            if (await source.ReadAtLeastAsync(iv, iv.Length, false) != AesBlockByteSize)
                throw new Exception("Cannot read IV");
            var salt = new byte[PasswordSaltByteSize];
            if (await source.ReadAtLeastAsync(salt, salt.Length, false) != PasswordSaltByteSize)
                throw new Exception("Cannot read key salt");

            var cacheKey = $"{authenticated}:{Convert.ToHexString(salt)}";
            if (!_decryptionKeys.TryGetValue(cacheKey, out var keys)) {
                keys = DeriveKeys(salt, authenticated);
                _decryptionKeys[cacheKey] = keys;
            }

            using var mac = authenticated ? IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, keys.MacKey!) : null;
            mac?.AppendData(header);
            mac?.AppendData(iv);
            mac?.AppendData(salt);
            var ciphertext = authenticated ? new MacTrailerReadStream(source, mac!) : null;

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            try {
                using var aes = CreateAes();
                using var decryptor = aes.CreateDecryptor(keys.EncryptionKey, iv);
                await using var csDecrypt = new CryptoStream(ciphertext ?? source, decryptor, CryptoStreamMode.Read, true);
                Stream reader = decompress ? new GZipStream(csDecrypt, CompressionMode.Decompress, true) : csDecrypt;
                await using (reader) {
                    var buffer = new byte[32768];
                    long processed = HeaderSize + AesBlockByteSize + PasswordSaltByteSize + AesBlockByteSize;
                    int read;
                    while ((read = await reader.ReadAsync(buffer)) > 0) {
                        token?.ThrowIfCancellationRequested();
                        hasher.AppendData(buffer, 0, read);
                        await destination.WriteAsync(buffer.AsMemory(0, read));

                        processed += read;
                        progressAction?.Invoke(processed);
                    }
                }
            } catch (Exception ex) when (ex is CryptographicException or InvalidDataException && ciphertext != null) {
                // Wrong key or corrupted data: report the authentication result
                await ciphertext!.ReadToEndAsync();
                VerifyMac(mac!, ciphertext);
                throw;
            }

            if (ciphertext != null) {
                await ciphertext.ReadToEndAsync();
                VerifyMac(mac!, ciphertext);
            }

            return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        } // Decrypt

        private static void VerifyMac(IncrementalHash mac, MacTrailerReadStream ciphertext)
        {
            if (!CryptographicOperations.FixedTimeEquals(mac.GetHashAndReset(), ciphertext.Trailer))
                throw new CryptographicException("Authentication failed: wrong password or corrupted data");
        }

        private static Aes CreateAes()
        {
            var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            return aes;
        }

        private static byte[] GenerateRandomBytes(int numberOfBytes)
        {
            return RandomNumberGenerator.GetBytes(numberOfBytes);
        }

        private Keys DeriveKeys(byte[] salt, bool withMacKey)
        {
            var bytes = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(_password), salt, PasswordIterationCount,
                HashAlgorithmName.SHA256, withMacKey ? KeyByteSize * 2 : KeyByteSize);
            return new Keys(salt, bytes[..KeyByteSize], withMacKey ? bytes[KeyByteSize..] : null);
        }

        /// <summary>
        /// Write-only stream that adds the written data to hashes
        /// </summary>
        private class HashingWriteStream : Stream
        {
            private readonly Stream _destination;
            private readonly IncrementalHash[] _hashes;

            public HashingWriteStream(Stream destination, params IncrementalHash[] hashes)
            {
                _destination = destination;
                _hashes = hashes;
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => _destination.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => _destination.FlushAsync(cancellationToken);
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                foreach (var hash in _hashes)
                    hash.AppendData(buffer, offset, count);
                _destination.Write(buffer, offset, count);
            }

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                foreach (var hash in _hashes)
                    hash.AppendData(buffer.Span);
                await _destination.WriteAsync(buffer, cancellationToken);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }
        }

        /// <summary>
        /// Read-only stream that returns the source data except the trailing MAC and adds the returned data to the MAC
        /// </summary>
        private class MacTrailerReadStream : Stream
        {
            private readonly Stream _source;
            private readonly IncrementalHash _mac;
            private readonly byte[] _pending = new byte[32768 + MacByteSize];
            private int _pendingLength;
            private bool _eof;

            public MacTrailerReadStream(Stream source, IncrementalHash mac)
            {
                _source = source;
                _mac = mac;
            }

            /// <summary>
            /// The MAC at the end of the source (available after the end of the stream has been reached)
            /// </summary>
            public byte[] Trailer { get; private set; } = Array.Empty<byte>();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                return ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                // Keep at least MacByteSize bytes: they could be the trailer
                while (_pendingLength <= MacByteSize && !_eof) {
                    var read = await _source.ReadAsync(_pending.AsMemory(_pendingLength), cancellationToken);
                    if (read == 0)
                        _eof = true;
                    else
                        _pendingLength += read;
                }

                var available = _pendingLength - MacByteSize;
                if (available <= 0) {
                    if (_pendingLength < MacByteSize)
                        throw new CryptographicException("Authentication failed: the file is truncated");
                    Trailer = _pending[..MacByteSize];
                    return 0;
                }

                var count = Math.Min(available, buffer.Length);
                _pending.AsSpan(0, count).CopyTo(buffer.Span);
                _mac.AppendData(_pending, 0, count);
                Buffer.BlockCopy(_pending, count, _pending, 0, _pendingLength - count);
                _pendingLength -= count;
                return count;
            }

            /// <summary>
            /// Read the remaining data, so that the MAC covers all the ciphertext and the trailer is available
            /// </summary>
            public async Task ReadToEndAsync()
            {
                var buffer = new byte[32768];
                while (await ReadAsync(buffer) > 0) {
                }
            }
        }
    }
}

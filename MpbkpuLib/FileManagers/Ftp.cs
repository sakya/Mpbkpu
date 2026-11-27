using MpbkpuLib.Interfaces;
using FluentFTP;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using FluentFTP.Client.BaseClient;
using MpbkpuLib.Utility;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers
{
    public class Ftp : IFileManager
    {
        public const string Scheme = "ftp://";

        private  AsyncFtpClient? _client;

        public string? Host { get; set; }
        public int Port { get; set; }
        public SslProtocols SslProtocols { get; set; } = SslProtocols.None;
        public NetworkCredential? Credentials { get; set; }
        /// <summary>
        /// Fail when the server has no TLS, instead of sending the password in clear (path option "tls=required")
        /// </summary>
        public bool RequireTls { get; set; }
        /// <summary>
        /// SHA-256 fingerprint (hex) of a certificate to trust even if it does not validate, e.g. a self-signed one
        /// (path option "fingerprint=...")
        /// </summary>
        public string? TrustedFingerprint { get; set; }

        /// <summary>
        /// Fingerprint of the last certificate rejected, reported in the connection error
        /// </summary>
        private string? _rejectedFingerprint;

        public void Dispose()
        {
            _client?.Dispose();
        }

        public bool IsCaseSensitive => true;

        public string GetScheme()
        {
            return Scheme;
        }

        public char PathSeparator => '/';

        public bool CanOpenDatabase => false;

        public bool SupportCompression => true;

        public string CombinePath(params string[] paths)
        {
            return Utility.Files.CombinePath(PathSeparator, paths);
        }

        public async Task<StorageSpace?> GetSpace(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            // AVBL is an extension (draft-peterson-streamlined-ftp-command-extensions): "213 <bytes>", no total
            var reply = await (await GetClient()).Execute($"AVBL {GetPath(path)}");
            return reply.Success && long.TryParse(reply.Message?.Trim(), out var bytes) ? new StorageSpace(bytes, null) : null;
        }

        public async Task<File> GetFileInfo(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var f = await GetItem(path);
            if (f is not { Type: FtpObjectType.File })
                throw new FileNotFoundException($"File {path} not found", path);
            return ToFile(f, path);
        }

        public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            if (!await client.DirectoryExists(GetPath(path)))
                throw new DirectoryNotFoundException($"Folder {path} not found");
            foreach (var f in await client.GetListing(GetPath(path))) {
                if (f.Type is not (FtpObjectType.Directory or FtpObjectType.File))
                    continue;
                // Full paths keep the form of the given path, so relative paths can be computed
                var fInfo = ToFile(f, CombinePath(path, f.Name));
                if (includeHidden || !fInfo.Hidden)
                    yield return fInfo;
            }
        }

        public async Task<bool> CreateDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            if (await client.DirectoryExists(GetPath(path)))
                return true;
            return await client.CreateDirectory(GetPath(path));
        }

        public async Task<bool> DeleteDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            if (!await client.DirectoryExists(GetPath(path)))
                throw new DirectoryNotFoundException($"Folder {path} not found");
            await client.DeleteDirectory(GetPath(path));
            return true;
        }

        public async Task<bool> DeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            if (!await client.FileExists(GetPath(path)))
                throw new FileNotFoundException($"File {path} not found", path);
            await client.DeleteFile(GetPath(path));
            return true;
        }

        public async Task<bool> DirectoryExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            return await client.DirectoryExists(GetPath(path));
        }

        public async Task<bool> FileExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = await GetClient();
            return await client.FileExists(GetPath(path));
        }

        public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null, DateTime? lastModified = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);

            filePath = GetPath(filePath);
            var client = await GetClient();
            var res = write ? await client.OpenWrite(filePath) : await client.OpenRead(filePath);
            return new FileSystemStream(this, res);
        }

        public async Task RenameFile(string? filePath, string? newName)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);
            if (string.IsNullOrEmpty(newName))
                throw new ArgumentNullException(newName);
            if (newName.Contains(PathSeparator))
                throw new ArgumentException($"The new name cannot contain '{PathSeparator}'", newName);

            filePath = GetPath(filePath);
            var client = await GetClient();
            if (!await client.FileExists(filePath))
                throw new FileNotFoundException($"File {filePath} not found", filePath);

            var parent = Files.GetParent(PathSeparator, filePath, out _);
            if (!await client.MoveFile(filePath, Utility.Files.CombinePath(PathSeparator, parent, newName),
                    FtpRemoteExists.Skip))
                throw new IOException($"Failed to rename file {filePath}: a file named {newName} already exists");
        }

        public async Task<bool> CloseFile(Stream stream)
        {
            var canWrite = stream.CanWrite;
            stream.Close();

            // The server confirms the transfer only after the data connection is closed
            var client = await GetClient();
            var reply = await client.GetReply(default);
            if (canWrite && !reply.Success)
                throw new IOException($"Upload failed: {reply.ErrorMessage}");

            return true;
        }

        #region private operations
        private async Task<AsyncFtpClient> GetClient()
        {
            if (_client == null) {
                _client = new AsyncFtpClient(Host, Credentials, Port);
                _client.Config.SocketKeepAlive = true;
                // TLS when the server supports it (always with RequireTls); certificates are validated, a certificate
                // that does not validate is accepted only when its fingerprint is the trusted one
                _client.Config.EncryptionMode = RequireTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.Auto;
                _client.Config.SslProtocols = SslProtocols;
                _client.Config.ValidateAnyCertificate = false;
                _client.ValidateCertificate += OnValidateCertificate;
            }

            if (!_client.IsConnected) {
                _rejectedFingerprint = null;
                try {
                    await _client.Connect();
                } catch (Exception ex) when (_rejectedFingerprint != null) {
                    throw new AuthenticationException($"The certificate of {Host} is not trusted. If it is the right one, " +
                                                      $"add \"?fingerprint={_rejectedFingerprint}\" to the path", ex);
                }
            }
            return _client;
        }

        private void OnValidateCertificate(BaseFtpClient control, FtpSslValidationEventArgs e)
        {
            if (e.PolicyErrors == SslPolicyErrors.None) {
                e.Accept = true;
                return;
            }

            var fingerprint = e.Certificate.GetCertHashString(HashAlgorithmName.SHA256);
            e.Accept = NormalizeFingerprint(TrustedFingerprint) == fingerprint;
            if (!e.Accept)
                _rejectedFingerprint = fingerprint;
        }

        /// <summary>
        /// A fingerprint as hex digits only, upper case ("AB:CD ef" -> "ABCDEF")
        /// </summary>
        public static string? NormalizeFingerprint(string? fingerprint)
        {
            return string.IsNullOrEmpty(fingerprint) ?
                null :
                new string(fingerprint.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
        }

        /// <summary>
        /// Information about a file or directory, null if it does not exist.
        /// The same commands of the listing are used (MLST/MLSD or LIST), so dates are consistent.
        /// </summary>
        private async Task<FtpListItem?> GetItem(string path)
        {
            var client = await GetClient();
            var ftpPath = GetPath(path);
            var item = await client.GetObjectInfo(ftpPath);
            if (item != null)
                return item;

            // Servers without MLST: look for the item in the listing of its parent
            var parent = Files.GetParent(PathSeparator, ftpPath, out var name);
            if (string.IsNullOrEmpty(name) || !await client.DirectoryExists(parent))
                return null;
            return (await client.GetListing(parent)).FirstOrDefault(i => i.Name == name);
        }

        private static File ToFile(FtpListItem f, string path)
        {
            return new File()
            {
                FullPath = path,
                Name = f.Name,
                Type = f.Type == FtpObjectType.Directory ? File.FileTyes.Directory : File.FileTyes.File,
                Size = f.Type == FtpObjectType.File ? f.Size : 0,
                Created = f.Created == DateTime.MinValue ? null : f.Created,
                LastModified = f.Modified == DateTime.MinValue ? null : f.Modified,
                // Unix convention
                Hidden = f.Name.StartsWith('.')
            };
        }

        private string GetPath(string path)
        {
            return Utility.Files.GetPath(PathSeparator, $"{Scheme}{Host}", path);
        }
        #endregion
    }
}

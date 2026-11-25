using MpbkpuLib.Interfaces;
using System.Net;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers
{
    public class Sftp : IFileManager
    {
        public const string Scheme = "sftp://";
        public const int DefaultPort = 22;

        public string? Host { get; set; }
        public int Port { get; set; } = DefaultPort;
        public NetworkCredential? Credentials { get; set; }
        public string? PrivateKeyFile { get; set; }

        /// <summary>
        /// Fingerprints of the known servers ("host:port fingerprint" per line): the first key of a server is trusted,
        /// a different key later is rejected
        /// </summary>
        public static string KnownHostsFile { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mpbkpu", "known_hosts");

        private static readonly object KnownHostsLock = new();
        private SftpClient? _client;

        public void Dispose()
        {
            if (_client == null)
                return;
            try {
                if (_client.IsConnected)
                    _client.Disconnect();
            } catch {
                // ignored
            }
            _client.Dispose();
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

        public async Task<File> GetFileInfo(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var f = await GetItem(path);
            if (f is not { IsRegularFile: true })
                throw new FileNotFoundException($"File {path} not found", path);
            return ToFile(f, path);
        }

        public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var client = GetClient();
            if (!await DirectoryExists(path))
                throw new DirectoryNotFoundException($"Folder {path} not found");
            await foreach (var f in client.ListDirectoryAsync(GetPath(path), CancellationToken.None)) {
                // The listing includes the directory itself and its parent
                if (f.Name is "." or "..")
                    continue;
                if (!f.IsDirectory && !f.IsRegularFile)
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

            if (await DirectoryExists(path))
                return true;
            var client = GetClient();
            await client.CreateDirectoryAsync(GetPath(path));
            return true;
        }

        public async Task<bool> DeleteDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            if (!await DirectoryExists(path))
                throw new DirectoryNotFoundException($"Folder {path} not found");
            // SFTP deletes only empty directories
            await foreach (var child in GetDirectoryContent(path)) {
                if (child.Type == File.FileTyes.Directory)
                    await DeleteDirectory(child.FullPath);
                else
                    await DeleteFile(child.FullPath);
            }
            await GetClient().DeleteDirectoryAsync(GetPath(path));
            return true;
        }

        public async Task<bool> DeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            if (!await FileExists(path))
                throw new FileNotFoundException($"File {path} not found", path);
            await GetClient().DeleteFileAsync(GetPath(path), CancellationToken.None);
            return true;
        }

        public async Task<bool> DirectoryExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            return (await GetItem(path))?.IsDirectory == true;
        }

        public async Task<bool> FileExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            return (await GetItem(path))?.IsRegularFile == true;
        }

        public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);

            filePath = GetPath(filePath);
            var client = GetClient();
            if (write)
                return await Task.Run( ()  => new FileSystemStream(this, client.Open(filePath, FileMode.Create, FileAccess.Write)));
            return await Task.Run(() => new FileSystemStream(this, client.Open(filePath, FileMode.Open, FileAccess.Read)));
        }

        public async Task RenameFile(string? filePath, string? newName)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);
            if (string.IsNullOrEmpty(newName))
                throw new ArgumentNullException(newName);
            if (newName.Contains(PathSeparator))
                throw new ArgumentException($"The new name cannot contain '{PathSeparator}'", newName);

            if (!await FileExists(filePath))
                throw new FileNotFoundException($"File {filePath} not found", filePath);

            var sftpPath = GetPath(filePath);
            var parentPath = Utility.Files.GetParent(PathSeparator, sftpPath, out _);
            var targetPath = Utility.Files.CombinePath(PathSeparator, parentPath, newName);
            if (await GetItem(targetPath) != null)
                throw new IOException($"A file named {newName} already exists");

            await GetClient().RenameFileAsync(sftpPath, targetPath, CancellationToken.None);
        }

        public Task<bool> CloseFile(Stream stream)
        {
            stream.Close();
            return Task.FromResult(true);
        }

        #region private operations
        private SftpClient GetClient()
        {
            if (_client == null) {
                if (string.IsNullOrEmpty(Host))
                    throw new Exception($"{nameof(Host)} cannot be empty");
                if (Credentials == null)
                    throw new Exception($"Credentials cannot be null");

                if (!string.IsNullOrEmpty(PrivateKeyFile)) {
                    PrivateKeyFile[] pks = { new (PrivateKeyFile) };
                    _client = new SftpClient(Host, Port, Credentials.UserName, pks);
                } else {
                    _client = new SftpClient(Host, Port, Credentials.UserName, Credentials.Password);
                }
                _client.HostKeyReceived += OnHostKeyReceived;
            }

            if (!_client.IsConnected)
                _client.Connect();
            return _client;
        }

        /// <summary>
        /// Trust on first use: the first key of a server is saved, a different key later is rejected
        /// </summary>
        private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
        {
            var host = $"{Host}:{Port}";
            lock (KnownHostsLock) {
                var known = System.IO.File.Exists(KnownHostsFile) ? System.IO.File.ReadAllLines(KnownHostsFile) : Array.Empty<string>();
                var saved = known.Select(l => l.Split(' ', 2))
                    .FirstOrDefault(p => p.Length == 2 && string.Equals(p[0], host, StringComparison.OrdinalIgnoreCase));
                if (saved != null) {
                    e.CanTrust = saved[1] == e.FingerPrintSHA256;
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(KnownHostsFile)!);
                System.IO.File.AppendAllLines(KnownHostsFile, new[] { $"{host} {e.FingerPrintSHA256}" });
                e.CanTrust = true;
            }
        }

        /// <summary>
        /// Information about a file or directory, null if it does not exist
        /// </summary>
        private async Task<ISftpFile?> GetItem(string path)
        {
            var client = GetClient();
            var sftpPath = GetPath(path);
            try {
                return await client.GetAsync(sftpPath, CancellationToken.None);
            } catch (SftpPathNotFoundException) {
                return null;
            }
        }

        private static File ToFile(ISftpFile f, string path)
        {
            return new File()
            {
                FullPath = path,
                Name = f.Name,
                Type = f.IsDirectory ? File.FileTyes.Directory : File.FileTyes.File,
                Size = f.IsRegularFile ? f.Length : 0,
                LastModified = f.LastWriteTimeUtc,
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

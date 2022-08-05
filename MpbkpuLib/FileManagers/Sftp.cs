using MpbkpuLib.Interfaces;
using System.Net;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers
{
    public class Sftp : IFileManager
    {
        private SftpClient? _client;

        public string? Host { get; set; }
        public int Port { get; set; }
        public NetworkCredential? Credentials { get; set; }
        public string? PrivateKeyFile { get; set; }

        public void Dispose()
        {

        }

        public bool CanOpenDatabase()
        {
            return false;
        }

        public string CombinePath(string? part1, string part2)
        {
            if (string.IsNullOrEmpty(part1))
                throw new ArgumentNullException(nameof(part1));
            if (string.IsNullOrEmpty(part2))
                throw new ArgumentNullException(nameof(part2));

            if (!part1.EndsWith("/"))
                part1 = $"{part1}/";
            return $"{part1}{part2}";
        }

        public string CombinePath(string? part1, string part2, string part3)
        {
            if (string.IsNullOrEmpty(part1))
                throw new ArgumentNullException(nameof(part1));
            if (string.IsNullOrEmpty(part2))
                throw new ArgumentNullException(nameof(part2));
            if (string.IsNullOrEmpty(part3))
                throw new ArgumentNullException(nameof(part3));

            if (!part1.EndsWith("/"))
                part1 = $"{part1}/";
            if (!part2.EndsWith("/"))
                part2 = $"{part2}/";
            return $"{part1}{part2}{part3}";
        }

        public string CombinePath(string? part1, string part2, string part3, string part4)
        {
            if (string.IsNullOrEmpty(part1))
                throw new ArgumentNullException(nameof(part1));
            if (string.IsNullOrEmpty(part2))
                throw new ArgumentNullException(nameof(part2));
            if (string.IsNullOrEmpty(part3))
                throw new ArgumentNullException(nameof(part3));
            if (string.IsNullOrEmpty(part4))
                throw new ArgumentNullException(nameof(part4));

            if (!part1.EndsWith("/"))
                part1 = $"{part1}/";
            if (!part2.EndsWith("/"))
                part2 = $"{part2}/";
            if (!part3.EndsWith("/"))
                part3 = $"{part3}/";
            return $"{part1}{part2}{part3}{part4}";
        }

        public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = GetClient();
            foreach (var f in await ListDirectoryAsync(client, path)) {
                var fInfo = new File()
                {
                    FullPath = $"sftp://{Host}{f.FullName}",
                    Name = f.Name,
                    Size = f.Length,
                    LastModified = f.Attributes.LastWriteTime
                };

                if (f.Attributes.IsDirectory) {
                    fInfo.Type = File.FileTyes.Directory;
                } else if (f.Attributes.IsRegularFile) {
                    fInfo.Type = File.FileTyes.File;
                } else {
                    continue;
                }
                yield return fInfo;
            }
        }

        public async Task<bool> CreateDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = GetClient();
            if (await DirectoryExists(path))
                throw new Exception($"Cannot create '{path}' because a directory with the same name already exists");
            await Task.Run( () => client.CreateDirectory(path));
            return true;
        }

        public async Task<bool> DeleteDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = GetClient();
            var fi = await Task.Run( () => client.Get(path));
            if (fi.Attributes.IsDirectory)
                await Task.Run( () => client.DeleteDirectory(path));
            else
                return false;

            return true;
        }

        public async Task<bool> DeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = GetClient();
            var fi = await Task.Run( () => client.Get(path));
            if (fi.Attributes.IsRegularFile)
                await Task.Run( () => client.DeleteFile(path));
            else
                return false;

            return true;
        }

        public async Task<bool> DirectoryExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = GetClient();
            try {
                var fi = await Task.Run(() => client.Get(path));
                return fi.Attributes.IsDirectory;
            } catch {
                return false;
            }
        }

        public async Task<bool> FileExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = GetClient();
            try {
                var fi = await Task.Run(() => client.Get(path));
                return fi.Attributes.IsRegularFile;
            } catch {
                return false;
            }
        }

        public async Task<Stream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);

            filePath = GetPath(filePath);
            var client = GetClient();
            if (write)
                return await Task.Run( ()  => client.Open(filePath, FileMode.Create, FileAccess.Write));
            return await Task.Run(() => client.Open(filePath, FileMode.Open, FileAccess.Read));
        }

        #region private operations
        private SftpClient GetClient()
        {
            if (_client == null) {
                if (Credentials == null)
                    throw new Exception($"Credentials cannot be null");

                if (!string.IsNullOrEmpty(PrivateKeyFile)) {
                    PrivateKeyFile[] pks = { new (PrivateKeyFile) };
                    _client = new SftpClient(Host, Port, Credentials.UserName, pks);
                } else {
                    _client = new SftpClient(Host, Port, Credentials.UserName, Credentials.Password);
                }
            }

            if (!_client.IsConnected)
                _client.Connect();
            return _client;
        }

        private string GetPath(string position)
        {
            if (position.StartsWith($"sftp://{Host}")) {
                return position.Remove(0, $"sftp://{Host}".Length);
            }
            return position;
        }

        private async Task<IEnumerable<SftpFile>> ListDirectoryAsync(SftpClient client, string path)
        {
            return await Task.Factory.FromAsync<IEnumerable<SftpFile>>((callback, obj) => client.BeginListDirectory(path, callback, obj), client.EndListDirectory, null);
        }
        #endregion
    }
}
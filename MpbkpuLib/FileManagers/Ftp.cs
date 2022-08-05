using MpbkpuLib.Interfaces;
using FluentFTP;
using System.Net;
using System.Security.Authentication;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers
{
    public class Ftp : IFileManager
    {
        private  FtpClient? _client;

        public string? Host { get; set; }
        public int Port { get; set; }
        public SslProtocols SslProtocols { get; set; } = SslProtocols.None;
        public NetworkCredential? Credentials { get; set; }

        public void Dispose()
        {
            _client?.Dispose();
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
            foreach (var f in await client.GetListingAsync(path)) {
                var fInfo = new File()
                {
                    FullPath = $"ftp://{Host}{f.FullName}",
                    Name = f.Name,
                    Size = f.Size,
                    Created = f.Created,
                    LastModified = f.Modified
                };

                if (f.Type == FtpObjectType.Directory) {
                    fInfo.Type = File.FileTyes.Directory;
                } else if (f.Type == FtpObjectType.File) {
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
            return await client.CreateDirectoryAsync(path);
        }

        public async Task<bool> DeleteDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = GetClient();
            var fi = await client.GetObjectInfoAsync(path);
            if (fi.Type == FtpObjectType.Directory)
                await client.DeleteDirectoryAsync(path);
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
            var fi = await client.GetObjectInfoAsync(path);
            if (fi.Type == FtpObjectType.File)
                await client.DeleteFileAsync(path);
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
            return await client.DirectoryExistsAsync(path);
        }

        public async Task<bool> FileExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = GetClient();
            return await client.FileExistsAsync(path);
        }

        public async Task<Stream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);

            filePath = GetPath(filePath);
            var client = GetClient();
            Stream? res;
            if (!write)
                res = await client.OpenReadAsync(filePath);
            else
                res = await client.OpenWriteAsync(filePath);

            return res;
        }

        #region private operations
        private FtpClient GetClient()
        {
            if (_client == null) {
                _client = new FtpClient(Host, Port, Credentials);
                _client.SocketKeepAlive = true;
                _client.EncryptionMode = FtpEncryptionMode.Auto;
                _client.SslProtocols = SslProtocols;
                _client.SslBuffering = FtpsBuffering.Off;
                _client.ValidateAnyCertificate = true;
            }

            if (!_client.IsConnected)
                _client.Connect();
            return _client;
        }

        private string GetPath(string position)
        {
            if (position.StartsWith($"ftp://{Host}"))
                return position.Remove(0, $"ftp://{Host}".Length);

            return position;
        }
        #endregion
    }
}
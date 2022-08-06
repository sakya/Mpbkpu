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

        public string CombinePath(params string[] paths)
        {
            string res = string.Empty;
            foreach (var p in paths) {
                if (string.IsNullOrEmpty(p))
                    throw new Exception("Path cannot be empty");

                var part = p;
                if (!part.EndsWith("/"))
                    part = $"{part}/";
                res = $"{res}{part}";
            }

            return res;
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

        public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
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

            return new FileSystemStream(this, res);
        }

        public async Task<bool> CloseFile(Stream stream)
        {
            bool canWrite = stream.CanWrite;
            stream.Close();

            if (canWrite) {
                var client = GetClient();
                await client.GetReplyAsync(default);
            }

            return true;
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
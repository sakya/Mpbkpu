using MpbkpuLib.Interfaces;
using FluentFTP;
using System.Net;
using System.Security.Authentication;
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

        public async Task<File> GetFileInfo(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = await GetClient();
            var f = await client.GetObjectInfo(path, true);
            if (f == null || f.Type != FtpObjectType.File)
                throw new Exception($"File {path} not found");

            return new File() {
                FullPath = $"{Scheme}{Host}{f.FullName}",
                Name = f.Name,
                Type = File.FileTyes.File,

                Size = f.Size,
                LastModified = f.Modified,
                Created = f.Created,
            };
        }

        public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = await GetClient();
            foreach (var f in await client.GetListing(path)) {
                var fInfo = new File()
                {
                    FullPath = $"{Scheme}{Host}{f.FullName}",
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
            var client = await GetClient();
            if (await DirectoryExists(path))
                throw new Exception($"Cannot create '{path}' because a directory with the same name already exists");
            return await client.CreateDirectory(path);
        }

        public async Task<bool> DeleteDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = await GetClient();
            var fi = await client.GetObjectInfo(path);
            if (fi.Type == FtpObjectType.Directory)
                await client.DeleteDirectory(path);
            else
                return false;

            return true;
        }

        public async Task<bool> DeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = await GetClient();
            var fi = await client.GetObjectInfo(path);
            if (fi.Type == FtpObjectType.File)
                await client.DeleteFile(path);
            else
                return false;

            return true;
        }

        public async Task<bool> DirectoryExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = await GetClient();
            return await client.DirectoryExists(path);
        }

        public async Task<bool> FileExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            path = GetPath(path);
            var client = await GetClient();
            return await client.FileExists(path);
        }

        public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);

            filePath = GetPath(filePath);
            var client = await GetClient();
            Stream? res;
            if (!write)
                res = await client.OpenRead(filePath);
            else
                res = await client.OpenWrite(filePath);

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

            var f = await client.GetObjectInfo(filePath, true);
            if (f == null || f.Type != FtpObjectType.File)
                throw new FileNotFoundException($"File {filePath} not found", filePath);

            var parent = Files.GetParent(PathSeparator, filePath, out _);
            if (!await client.MoveFile(filePath, Utility.Files.CombinePath(PathSeparator, parent, newName),
                    FtpRemoteExists.Skip))
                throw new Exception($"Failed to move rename file {filePath}");
        }

        public async Task<bool> CloseFile(Stream stream)
        {
            bool canWrite = stream.CanWrite;
            stream.Close();

            if (canWrite) {
                var client = await GetClient();
                await client.GetReply(default);
            }

            return true;
        }

        #region private operations
        private async Task<AsyncFtpClient> GetClient()
        {
            if (_client == null) {
                _client = new AsyncFtpClient(Host, Credentials, Port);
                _client.Config.SocketKeepAlive = true;
                _client.Config.EncryptionMode = FtpEncryptionMode.Auto;
                _client.Config.SslProtocols = SslProtocols;
                _client.Config.ValidateAnyCertificate = true;
            }

            if (!_client.IsConnected)
                await _client.Connect();
            return _client;
        }

        private string GetPath(string path)
        {
            return Utility.Files.GetPath(PathSeparator, $"{Scheme}{Host}", path);
        }
        #endregion
    }
}
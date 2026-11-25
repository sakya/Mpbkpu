using EzSmb;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers
{
    public class Smb : IFileManager
    {
        public const string Scheme = "smb://";

        private readonly string? _domainName;
        private readonly string? _username;
        private readonly string? _password;

        public Smb(string domainName, string username, string password, string host)
        {
            _domainName = domainName;
            _username = username;
            _password = password;
            Host = host;
        }

        public void Dispose()
        {
        }

        public bool IsCaseSensitive => false;

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

        public string? Host { get; }

        public Task<StorageSpace?> GetSpace(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            // EzSmb does not expose the free space of a share
            return Task.FromResult<StorageSpace?>(null);
        }

        public async Task<File> GetFileInfo(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using var node = await GetNode(path);
            if (node is not { Type: NodeType.File })
                throw new FileNotFoundException($"File {path} not found", path);
            return ToFile(node, path);
        }

        public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using var dir = await GetNode(path);
            if (dir is not { Type: NodeType.Folder })
                throw new DirectoryNotFoundException($"Folder {path} not found");

            // The library already skips "." and ".."
            var children = await dir.GetList() ?? throw new IOException($"Cannot read folder {path}");
            foreach (var node in children) {
                using (node) {
                    if (node.Type is not (NodeType.Folder or NodeType.File))
                        continue;
                    // Full paths keep the form of the given path, so relative paths can be computed
                    // (the library returns paths with the IP address instead of the host name)
                    var fInfo = ToFile(node, CombinePath(path, node.Name));
                    if (includeHidden || !fInfo.Hidden)
                        yield return fInfo;
                }
            }
        }

        public async Task<bool> CreateDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using (var existing = await GetNode(path)) {
                if (existing != null) {
                    if (existing.Type == NodeType.Folder)
                        return true;
                    throw new IOException($"A file named {path} already exists");
                }
            }

            var parentPath = Utility.Files.GetParent(PathSeparator, GetPath(path), out var dirName);
            using var parent = await GetNode(parentPath);
            if (parent is not { Type: NodeType.Folder })
                throw new DirectoryNotFoundException($"Folder {parentPath} not found");
            using var created = await parent.CreateFolder(dirName);
            if (created == null)
                throw new IOException($"Cannot create folder {path}");
            return true;
        }

        public async Task<bool> DeleteDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using (var node = await GetNode(path)) {
                if (node is not { Type: NodeType.Folder })
                    throw new DirectoryNotFoundException($"Folder {path} not found");
            }

            // SMB deletes only empty folders
            await foreach (var child in GetDirectoryContent(path)) {
                if (child.Type == File.FileTyes.Directory)
                    await DeleteDirectory(child.FullPath);
                else
                    await DeleteFile(child.FullPath);
            }

            using var folder = await GetNode(path);
            if (folder == null || !await folder.Delete())
                throw new IOException($"Cannot delete folder {path}");
            return true;
        }

        public async Task<bool> DeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using var node = await GetNode(path);
            if (node is not { Type: NodeType.File })
                throw new FileNotFoundException($"File {path} not found", path);
            if (!await node.Delete())
                throw new IOException($"Cannot delete file {path}");
            return true;
        }

        public async Task<bool> DirectoryExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using var node = await GetNode(path);
            return node?.Type == NodeType.Folder;
        }

        public async Task<bool> FileExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using var node = await GetNode(path);
            return node?.Type == NodeType.File;
        }

        public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);

            if (!write) {
                var node = await GetNode(filePath);
                if (node is not { Type: NodeType.File }) {
                    node?.Dispose();
                    throw new FileNotFoundException($"File {filePath} not found", filePath);
                }
                return new FileSystemStream(this, node.GetReaderStream()
                                                  ?? throw new IOException($"Cannot read file {filePath}"));
            }

            if (size == null)
                throw new ArgumentNullException(nameof(size));

            var parentPath = Utility.Files.GetParent(PathSeparator, GetPath(filePath), out var fileName);
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException($"Invalid path {filePath}");
            // The library does not truncate an existing file: delete it first
            if (await FileExists(filePath))
                await DeleteFile(filePath);

            var parent = await GetNode(parentPath);
            if (parent is not { Type: NodeType.Folder }) {
                parent?.Dispose();
                throw new DirectoryNotFoundException($"Folder {parentPath} not found");
            }

            // The library writes stream.Length bytes reading the stream in order
            var res = new UploadStream(size.Value, async (stream, _) =>
            {
                using (parent) {
                    if (!await parent.Write(stream, fileName))
                        throw new IOException($"Upload of {filePath} failed");
                }
            });
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

            using var node = await GetNode(filePath);
            if (node is not { Type: NodeType.File })
                throw new FileNotFoundException($"File {filePath} not found", filePath);

            var parentPath = Utility.Files.GetParent(PathSeparator, GetPath(filePath), out _);
            using (var target = await GetNode(CombinePath(parentPath, newName))) {
                if (target != null)
                    throw new IOException($"A file named {newName} already exists");
            }

            // The new path is relative to the parent folder of the node
            using var moved = await node.Move(newName);
            if (moved == null)
                throw new IOException($"Cannot rename {filePath} to {newName}");
        }

        public async Task<bool> CloseFile(Stream stream)
        {
            try {
                if (stream is UploadStream us)
                    await us.CompleteAsync();
            } finally {
                stream.Close();
            }
            return true;
        }

        #region private operations
        private EzSmb.Params.ParamSet GetParamSet()
        {
            return new EzSmb.Params.ParamSet()
            {
                DomainName = _domainName,
                UserName = _username,
                Password = _password,
                SmbType = EzSmb.Params.Enums.SmbType.Smb2
            };
        }

        /// <summary>
        /// The node at a path, null if it does not exist
        /// </summary>
        private Task<Node?> GetNode(string path)
        {
            return Node.GetNode(GetPath(path), GetParamSet(), false)!;
        }

        private static File ToFile(Node node, string path)
        {
            return new File()
            {
                FullPath = path,
                Name = node.Name,
                Type = node.Type == NodeType.Folder ? File.FileTyes.Directory : File.FileTyes.File,
                Size = node.Type == NodeType.File ? node.Size ?? 0 : 0,
                Created = node.Created,
                LastModified = node.Updated,
            };
        }

        /// <summary>
        /// Path for the library: host/share/path
        /// </summary>
        private string GetPath(string path)
        {
            return Utility.Files.GetPath(PathSeparator, Scheme, path).TrimStart(PathSeparator);
        }
        #endregion
    }
}

using CG.Web.MegaApiClient;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class Mega : IFileManager
{
    public const string Scheme = "mega://";

    private readonly string? _username;
    private readonly string? _password;
    private MegaApiClient? _client;
    /// <summary>
    /// Nodes of the account, read once and kept updated by the operations of this instance
    /// </summary>
    private List<INode>? _nodes;

    public Mega(string username, string password)
    {
        if (string.IsNullOrEmpty(username))
            throw new ArgumentNullException(nameof(username));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentNullException(nameof(password));
        _username = username;
        _password = password;
    }

    public void Dispose()
    {
        if (_client?.IsLoggedIn == true) {
            try {
                _client?.Logout();
            } catch {
                // ignored
            }
        }
    }

    public bool IsCaseSensitive => true;

    public string GetScheme()
    {
        return Scheme;
    }

    public char PathSeparator => '/';

    public bool CanOpenDatabase => false;

    public bool SupportCompression => false;

    public string CombinePath(params string[] paths)
    {
        return Utility.Files.CombinePath(PathSeparator, paths);
    }

    public async Task<StorageSpace?> GetSpace(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var info = await (await GetClient()).GetAccountInformationAsync();
        return new StorageSpace(Math.Max(0, info.TotalQuota - info.UsedQuota), info.TotalQuota);
    }

    public async Task<bool> CreateDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var existing = await GetNode(path);
        if (existing != null) {
            // MEGA allows duplicate names: never create a second folder with the same name
            if (existing.Type == NodeType.Directory)
                return true;
            throw new IOException($"A file named {path} already exists");
        }

        var client = await GetClient();
        var parent = await GetParentNode(path, out var childName);
        var node = await client.CreateFolderAsync(childName, parent);
        _nodes!.Add(node);
        return true;
    }

    public async Task<bool> DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var node = await GetNode(path);
        if (node is not { Type: NodeType.Directory })
            throw new DirectoryNotFoundException($"Folder {path} not found");
        await DeleteNode(node);
        return true;
    }

    public async Task<bool> DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var node = await GetNode(path);
        if (node is not { Type: NodeType.File })
            throw new FileNotFoundException($"File {path} not found", path);
        await DeleteNode(node);
        return true;
    }

    public async Task<bool> DirectoryExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var node = await GetNode(path);
        return node?.Type is NodeType.Directory or NodeType.Root;
    }

    public async Task<bool> FileExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var node = await GetNode(path);
        return node?.Type == NodeType.File;
    }

    public async Task<File> GetFileInfo(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var node = await GetNode(path);
        if (node == null)
            throw new FileNotFoundException($"File {path} not found", path);
        return ToFile(node, path);
    }

    public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var parent = await GetNode(path);
        if (parent is not { Type: NodeType.Directory or NodeType.Root })
            throw new DirectoryNotFoundException($"Folder {path} not found");

        foreach (var node in GetChildren(parent).ToList()) {
            yield return ToFile(node, CombinePath(path, node.Name));
        }
    }

    public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(filePath);

        var client = await GetClient();
        if (!write) {
            var node = await GetNode(filePath);
            if (node is not { Type: NodeType.File })
                throw new FileNotFoundException($"File {filePath} not found", filePath);
            return new FileSystemStream(this, await client.DownloadAsync(node));
        }

        if (size == null)
            throw new ArgumentNullException(nameof(size));

        // Overwrite like a file system: MEGA would keep both files
        if (await FileExists(filePath))
            await DeleteFile(filePath);
        var parent = await GetParentNode(filePath, out var childName);
        var res = new UploadStream(size.Value, (stream, token) =>
        {
            var node = client.Upload(stream, childName, parent, null, token);
            lock (_nodes!) {
                _nodes.Add(node);
            }
            return Task.CompletedTask;
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

        var client = await GetClient();
        var sourceNode = await GetNode(filePath);
        if (sourceNode == null)
            throw new FileNotFoundException($"File {filePath} not found", filePath);
        var parentPath = Utility.Files.GetParent(PathSeparator, GetPath(filePath), out _);
        if (await GetNode(CombinePath(parentPath, newName)) != null)
            throw new IOException($"A file named {newName} already exists");

        var renamed = await client.RenameAsync(sourceNode, newName);
        lock (_nodes!) {
            _nodes.Remove(sourceNode);
            _nodes.Add(renamed);
        }
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
    private async Task<MegaApiClient> GetClient()
    {
        if (_client == null) {
            _client = new MegaApiClient(new WebClient(30000));
            await _client.LoginAsync(_username, _password);
        }

        return _client;
    }

    private static File ToFile(INode node, string path)
    {
        return new File()
        {
            FullPath = path,
            Name = node.Name,
            Type = node.Type == NodeType.File ? File.FileTyes.File : File.FileTyes.Directory,
            Size = node.Type == NodeType.File ? node.Size : 0,
            Created = node.CreationDate,
            LastModified = node.ModificationDate
        };
    }

    /// <summary>
    /// Path without the scheme, always starting with the separator
    /// </summary>
    private string GetPath(string path)
    {
        path = Utility.Files.GetPath(PathSeparator, Scheme, path);
        return path.StartsWith(PathSeparator) ? path : $"{PathSeparator}{path}";
    }

    private IEnumerable<INode> GetChildren(INode parent)
    {
        lock (_nodes!) {
            return _nodes.Where(n => n.ParentId == parent.Id).ToList();
        }
    }

    /// <summary>
    /// Find a node from its path: paths always start from the root of the Cloud Drive
    /// </summary>
    private async Task<INode?> GetNode(string path)
    {
        var client = await GetClient();
        _nodes ??= (await client.GetNodesAsync()).ToList();

        INode? node;
        lock (_nodes) {
            node = _nodes.FirstOrDefault(n => n.Type == NodeType.Root);
        }
        foreach (var name in GetPath(path).Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            if (node == null)
                return null;
            // Duplicate names are possible: prefer folders, then the most recent item
            node = GetChildren(node)
                .Where(n => n.Name == name)
                .OrderBy(n => n.Type == NodeType.File)
                .ThenByDescending(n => n.ModificationDate ?? n.CreationDate)
                .FirstOrDefault();
        }

        return node;
    }

    private Task<INode> GetParentNode(string path, out string childName)
    {
        var parentPath = Utility.Files.GetParent(PathSeparator, GetPath(path), out var name);
        childName = name ?? throw new ArgumentException($"Invalid path {path}");
        return GetExistingFolder(parentPath);
    }

    private async Task<INode> GetExistingFolder(string path)
    {
        var node = await GetNode(path);
        if (node is not { Type: NodeType.Directory or NodeType.Root })
            throw new DirectoryNotFoundException($"Folder {path} not found");
        return node;
    }

    /// <summary>
    /// Permanently delete a node (not moved to the trash) and remove it and its descendants from the cache
    /// </summary>
    private async Task DeleteNode(INode node)
    {
        await (await GetClient()).DeleteAsync(node, false);
        lock (_nodes!) {
            var removed = new HashSet<string> { node.Id };
            bool changed;
            do {
                changed = false;
                foreach (var n in _nodes.Where(n => !removed.Contains(n.Id) && removed.Contains(n.ParentId)).ToList())
                    changed |= removed.Add(n.Id);
            } while (changed);
            _nodes.RemoveAll(n => removed.Contains(n.Id));
        }
    }
    #endregion
}

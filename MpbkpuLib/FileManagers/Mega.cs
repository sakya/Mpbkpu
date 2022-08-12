using CG.Web.MegaApiClient;
using MpbkpuLib.Interfaces;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class Mega : IFileManager
{
    #region classes
    private class WriterStream : Stream
    {
        private long _pos;

        private readonly List<byte> _buffer = new();
        private readonly SemaphoreSlim _bufferSema = new(1,1);

        public WriterStream(long length)
        {
            Length = length;
        }

        public Task? WriteTask { get; set; }
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length { get; }

        public override long Position {
            get => _pos;
            set => _pos = value;
        }

        protected override void Dispose(bool disposing)
        {
            WriteTask?.Dispose();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= Length)
                return 0;

            _bufferSema.Wait();
            int readCount = 0;
            for (int i = 0; i < count; i++) {
                if (_buffer.Count > 0) {
                    buffer[offset + i] = _buffer[0];
                    _buffer.RemoveAt(0);

                    _pos++;
                    readCount++;
                    if (_pos >= Length)
                        break;
                } else {
                    _bufferSema.Release();
                    Thread.Sleep(20);
                    _bufferSema.Wait();
                    i--;
                }
            }
            _bufferSema.Release();
            return readCount;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return offset;
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            while(_buffer.Count > 0)
                Thread.Sleep(10);

            _bufferSema.Wait();
            for (int i = 0; i < count; i++) {
                _buffer.Add(buffer[offset + i]);
            }
            _bufferSema.Release();
        }

        public async Task WaitTask()
        {
            if (WriteTask != null)
                await WriteTask;
        }
    }
    #endregion

    private readonly string? _username;
    private readonly string? _password;
    private MegaApiClient? _client;
    private IEnumerable<INode>? _nodes;

    public Mega(string username, string password)
    {
        _username = username;
        _password = password;
    }

    public void Dispose()
    {
        if (_client?.IsLoggedIn == true)
            _client?.Logout();
    }

    public bool CanOpenDatabase()
    {
        return false;
    }

    public bool SupportCompression()
    {
        return false;
    }

    public string CombinePath(params string[] paths)
    {
        return Utility.Files.CombinePath('/', paths);
    }

    public async Task<bool> CreateDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        var parentPath = GetParent(GetPath(path), out var childName);
        var parent = await GetNode(parentPath);
        if (parent == null)
            throw new Exception($"Folder {parentPath} not found");
        return await client.CreateFolderAsync(childName, parent) != null;
    }

    public async Task<bool> DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        path = GetPath(path);
        var client = await GetClient();
        var node = await GetNode(path);
        if (node == null || node.Type != NodeType.Directory)
            throw new Exception($"Folder {path} not found");
        await client.DeleteAsync(node, false);

        return true;
    }

    public async Task<bool> DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        path = GetPath(path);
        var client = await GetClient();
        var node = await GetNode(path);
        if (node == null || node.Type != NodeType.File)
            throw new Exception($"File {path} not found");
        await client.DeleteAsync(node, false);

        return true;
    }

    public async Task<bool> DirectoryExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var node = await GetNode(path);
        return node?.Type == NodeType.Directory;
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

        var fInfo = new File()
        {
            FullPath = path,
            Name = node.Name,
            Type = File.FileTyes.File,
            Size = node.Size,

            Created = node.CreationDate,
            LastModified = node.ModificationDate
        };

        return fInfo;
    }

    public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var parent = await GetNode(path);
        if (parent != null) {
            foreach (var node in _nodes!.Where(n => n.ParentId == parent.Id)) {
                var fInfo = new File()
                {
                    FullPath = $"{path}/{node.Name}",
                    Name = node.Name,
                    Type = node.Type == NodeType.Directory ? File.FileTyes.Directory : File.FileTyes.File,
                    Size = node.Size,

                    Created = node.CreationDate,
                    LastModified = node.ModificationDate
                };
                yield return fInfo;
            }
        }
    }

    public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(filePath);

        filePath = GetPath(filePath);
        var client = await GetClient();
        Stream? res;
        if (!write) {
            var node = await GetNode(filePath);
            if (node == null)
                throw new FileNotFoundException($"File {filePath} not found", filePath);
            res = client.Download(node);
        } else {
            if (size == null)
                throw new ArgumentNullException(nameof(size));

            var parentPath = GetParent(filePath, out var childName);
            var parent = await GetNode(parentPath);
            if (parent == null)
                throw new Exception($"Folder {parentPath} not found");

            var ws = new WriterStream(size.Value);
            ws.WriteTask = client.UploadAsync(ws, childName, parent);
            res = ws;
        }

        return new FileSystemStream(this, res);
    }

    public async Task<bool> CloseFile(Stream stream)
    {
        if (stream is WriterStream ws) {
            await ws.WaitTask();
        }
        stream.Close();

        return true;
    }

    #region private operations
    private async Task<MegaApiClient> GetClient()
    {
        if (_client == null) {
            _client = new MegaApiClient(new WebClient(20000));
            await _client.LoginAsync(_username, _password);
        }

        return _client;
    }

    private string GetPath(string position)
    {
        if (position.StartsWith($"mega://"))
            return position.Remove(0, $"mega://".Length);

        return position;
    }

    private string[] SplitPath(string position)
    {
        return position.Split('/');
    }

    private async Task<INode?> GetNode(string path)
    {
        var client = await GetClient();
        _nodes = await client.GetNodesAsync();
        if (_nodes == null)
            return null;

        INode? node = null;
        foreach (var dir in SplitPath(GetPath(path))) {
            if (node == null) {
                if (string.IsNullOrEmpty(dir) || dir == "/")
                    node = _nodes.FirstOrDefault(n => n.Type == NodeType.Root);
                else
                    node = _nodes.FirstOrDefault(n => n.Name == dir);
            } else
                node = _nodes.FirstOrDefault(n => n.Name == dir && n.ParentId == node.Id);

            if (node == null)
                return null;
        }

        return node;
    }

    private string GetParent(string position, out string? childName)
    {
        return Utility.Files.GetParent('/', GetPath(position), out childName);
    }
    #endregion
}
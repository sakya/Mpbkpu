using EzSmb;
using MpbkpuLib.Interfaces;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers
{
    public class Smb : IFileManager
    {
        private readonly string? _domainName;
        private readonly string? _username;
        private readonly string? _password;

        #region classes

        private class WriterStream : Stream
        {
            private bool _abort;
            private readonly string _fileName;
            private readonly Node? _node;
            private Task? _writeTask;
            private long _pos;
            private long _length;
            private long _writtenBytes;

            private readonly List<byte> _buffer = new List<byte>();
            private readonly SemaphoreSlim _bufferSema = new SemaphoreSlim(1,1);

            public WriterStream(Node node, string fileName, long? size)
            {
                _node = node;
                _fileName = fileName;
                if (size.HasValue)
                    _length = size.Value;
            }

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => _length;

            public override long Position {
                get => _pos;
                set => _pos = value;
            }

            protected override void Dispose(bool disposing)
            {
                _writeTask?.Dispose();
                _node?.Dispose();
                base.Dispose();
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_abort && _writtenBytes < _length || _pos >= _length)
                    return 0;

                _bufferSema.Wait();
                int readCount = 0;
                for (int i = 0; i < count; i++) {
                    if (_buffer.Count > 0) {
                        buffer[offset + i] = _buffer[0];
                        _buffer.RemoveAt(0);

                        _pos++;
                        readCount++;
                        if (_pos >= _length)
                            break;
                    } else {
                        _bufferSema.Release();
                        Thread.Sleep(10);
                        _bufferSema.Wait();
                        i--;
                        if (_abort)
                            break;
                    }
                }
                _bufferSema.Release();
                return readCount;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new InvalidOperationException();
            }

            public override void SetLength(long value)
            {
                _length = value;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_abort)
                    return;

                if (_node == null)
                    throw new Exception("Node is null");

                if (_writeTask == null)
                    _writeTask = _node.Write(this, _fileName);

                while(_buffer.Count >= 0)
                    Thread.Sleep(20);

                _bufferSema.Wait();
                for (int i = 0; i < count; i++) {
                    _buffer.Add(buffer[offset + i]);
                }
                _writtenBytes += count;
                _bufferSema.Release();
            }

            public async Task WaitTask()
            {
                if (_writeTask != null)
                    await _writeTask;
            }

            public void Abort()
            {
                _abort = true;
            }
        }
        #endregion

        public void Dispose()
        {

        }

        public Smb(string domainName, string username, string password, string host)
        {
            _domainName = domainName;
            _username = username;
            _password = password;
            Host = host;
        }

        public bool CanOpenDatabase()
        {
            return false;
        }

        public bool SupportCompression()
        {
            return true;
        }

        public string CombinePath(params string[] paths)
        {
            return Utility.Files.CombinePath('/', paths);
        }

        public string? Host { get; }

        public async Task<File> GetFileInfo(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using var node = await Node.GetNode(GetPath(path), GetParamSet(), true);
            if (node.Type != NodeType.File)
                throw new Exception($"File {path} not found");

            var fInfo = new File()
            {
                FullPath = $"smb://{node.FullPath.Replace("\\", "/")}",
                Name = node.Name,
                Type = File.FileTyes.File,
                Size = node.Size ?? 0,

                Created = node.Created,
                LastModified = node.Updated,
            };

            return fInfo;
        }

        public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using var dir = await Node.GetNode(GetPath(path), GetParamSet());
            foreach (var node in await dir.GetList()) {
                if (node.Name == ".." || node.Name == "." || node.Type != NodeType.Folder)
                    continue;

                var fInfo = new File()
                {
                    FullPath = $"smb://{node.FullPath.Replace("\\", "/")}",
                    Name = node.Name,
                    Size = node.Size ?? 0,

                    Created = node.Created,
                    LastModified = node.Updated,
                };

                if (node.Type == NodeType.Folder) {
                    fInfo.Type = File.FileTyes.Directory;
                } else if (node.Type == NodeType.File) {
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

            var node = await Node.GetNode(GetPath(path), GetParamSet());
            if (node?.Type == NodeType.Folder)
                throw new Exception($"Cannot create '{path}' because a directory with the same name already exists");

            node = await Node.GetNode(GetParent(path, out var dirName), GetParamSet());
            await node.CreateFolder(dirName);
            return true;
        }

        public async Task<bool> DeleteDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using var node = await Node.GetNode(GetPath(path), GetParamSet());
            if (node.Type == NodeType.Folder)
                return await node.Delete();
            return false;
        }

        public async Task<bool> DeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using var node = await Node.GetNode(GetPath(path), GetParamSet());
            if (node.Type == NodeType.File)
                return await node.Delete();
            return false;
        }

        public async Task<bool> DirectoryExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using var node = await Node.GetNode(GetPath(path), GetParamSet());
            return node?.Type == NodeType.Folder;
        }

        public async Task<bool> FileExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            using var node = await Node.GetNode(GetPath(path), GetParamSet());
            return node?.Type == NodeType.File;
        }

        public async Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);

            if (write) {
                var parent = GetParent(filePath, out var fileName);
                if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(fileName))
                    throw new Exception($"Invalid path {filePath}");

                var node = await Node.GetNode(GetPath(parent), GetParamSet());
                return new FileSystemStream(this, new WriterStream(node, fileName, size));
            } else {
                var node = await Node.GetNode(GetPath(filePath), GetParamSet());
                return new FileSystemStream(this, node.GetReaderStream());
            }
        }

        public async Task<bool> CloseFile(Stream stream)
        {
            if (stream is WriterStream ws) {
                ws.Abort();
                await ws.WaitTask();
            }
            stream.Close();
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

        private string GetPath(string position)
        {
            if (position.StartsWith("smb://"))
                return position.Remove(0, 6);
            return position;
        }

        private string GetParent(string position, out string? childName)
        {
            childName = null;
            position = GetPath(position);
            int idx = position.LastIndexOf("/", StringComparison.InvariantCultureIgnoreCase);
            if (idx >= 0) {
                childName = position.Substring(idx + 1);
                return position.Substring(0, idx);
            }

            throw new Exception($"Cannot get parent of {position}");
        }
        #endregion
    }
}
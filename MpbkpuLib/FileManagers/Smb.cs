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
        private readonly string? _host;

        public void Dispose()
        {

        }

        public Smb(string domainName, string username, string password, string host)
        {
            _domainName = domainName;
            _username = username;
            _password = password;
            _host = host;
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

        public string? Host
        {
            get { return _host; }
        }

        public async IAsyncEnumerable<DataModel.File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            var dir = await Node.GetNode(GetPath(path), GetParamSet());
            foreach (var node in await dir.GetList()) {
                if (node.Name == ".." || node.Name == "." || node.Type != NodeType.Folder)
                    continue;

                var fInfo = new DataModel.File()
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

            node =await Node.GetNode(GetParent(path), GetParamSet());
            await node.CreateFolder(GetDirectoryName(path));
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

        public Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            throw new NotImplementedException();
        }

        public Task<bool> CloseFile(Stream stream)
        {
            stream.Close();
            return Task.FromResult(true);
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

        private string GetParent(string position)
        {
            position = GetPath(position);
            int idx = position.LastIndexOf("/", StringComparison.InvariantCultureIgnoreCase);
            if (idx > 0)
                return position.Substring(0, idx);
            throw new Exception($"Cannot get parent of {position}");
        }

        private string GetDirectoryName(string position)
        {
            position = GetPath(position);
            int idx = position.LastIndexOf("/", StringComparison.InvariantCultureIgnoreCase);
            if (idx > 0)
                return position.Substring(idx);
            throw new Exception($"Cannot get directory name of {position}");
        }

        #endregion
    }
}
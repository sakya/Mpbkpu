using MediaFireApi;
using MediaFireApi.Exceptions;
using MediaFireApi.Models;
using MpbkpuLib.Interfaces;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class MediaFire : IFileManager
{
    private readonly string? _username;
    private readonly string? _password;
    private Client? _client;

    public MediaFire(string username, string password)
    {
        _username = username;
        _password = password;
    }

    public void Dispose()
    {
        _client?.Dispose();
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
        await client.FolderCreate(parentPath: parentPath, name: childName);
        return true;
    }

    public async Task<bool> DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var info = (await client.FolderGetInfo(folderPath: path))?.ToList();
        if (info?.Count == 1) {
            await client.FolderDelete(new []{ info[0].FolderKey });
            await client.FolderPurge( new []{ info[0].FolderKey });
            return true;
        }

        return false;
    }

    public async Task<bool> DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var info = (await client.FileGetInfo(filePath: path))?.ToList();
        if (info?.Count == 1) {
            await client.FolderDelete(new []{ info[0].QuickKey });
            await client.FolderPurge( new []{ info[0].QuickKey });
            return true;
        }

        return false;
    }

    public async Task<bool> DirectoryExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        try {
            var info = (await client.FolderGetInfo(folderPath: path))?.ToList();
            return info?.Count == 1;
        } catch (MediaFireApiException ex) {
            if (ex.ErrorCode == (int)ErrorCodes.ErrorInvalidFolderPath)
                return false;

            throw;
        }
    }

    public async Task<bool> FileExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        try {
            var info = (await client.FileGetInfo(filePath: path))?.ToList();
            return info?.Count == 1;
        } catch (MediaFireApiException ex) {
            if (ex.ErrorCode == (int)ErrorCodes.ErrorInvalidFilePath)
                return false;

            throw;
        }
    }

    public async Task<File> GetFileInfo(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);
        var info = (await client.FileGetInfo(filePath: path))?.ToList();
        if (info?.Count == 1) {
            var fInfo = new File()
            {
                FullPath = path,
                Name = info[0].Filename,
                Type = File.FileTyes.File,
                Size = info[0].Size,

                Created = info[0].Created
            };

            return fInfo;
        }
        throw new FileNotFoundException($"File {path} not found", path);
    }

    public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        var client = await GetClient();
        path = GetPath(path);

        // Read folders
        var content = await client.FolderGetContent(folderPath: path, contentType: FolderContentType.Folders);
        do {
            foreach (var f in content.Folders) {
                var fInfo = new File()
                {
                    FullPath = $"{path}/{f.Name}",
                    Name = f.Name,
                    Type = File.FileTyes.Directory,
                    Size = f.TotalSize,

                    Created = f.Created,
                };
                yield return fInfo;
            }
        } while (content.MoreChunks == YesNo.Yes);

        // Read files

    }

    public Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(filePath);


    }

    public Task<bool> CloseFile(Stream stream)
    {
        throw new NotImplementedException();
    }

    #region private operations
    private async Task<Client> GetClient()
    {
        if (_client == null) {
            _client = new Client(new ClientSettings());
            await _client.Login(_username, _password);
        }

        return _client;
    }

    private string GetPath(string path)
    {
        return Utility.Files.GetPath('/', "mediafire://", path);
    }

    private string GetParent(string position, out string? childName)
    {
        return Utility.Files.GetParent('/', GetPath(position), out childName);
    }
    #endregion
}
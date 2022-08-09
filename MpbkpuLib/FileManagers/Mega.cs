using CG.Web.MegaApiClient;
using MpbkpuLib.Interfaces;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class Mega : IFileManager
{
    private readonly string? _username;
    private readonly string? _password;
    private MegaApiClient? _client;

    public Mega(string username, string password)
    {
        _username = username;
        _password = password;
    }

    public void Dispose()
    {
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

    public Task<bool> CreateDirectory(string? path)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteDirectory(string? path)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteFile(string? path)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DirectoryExists(string? path)
    {
        throw new NotImplementedException();
    }

    public Task<bool> FileExists(string? path)
    {
        throw new NotImplementedException();
    }

    public async IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
    {
        var client = await GetClient();
        foreach (var node in await client.GetNodesAsync()) {
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

    public Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
    {
        throw new NotImplementedException();
    }

    public Task<bool> CloseFile(Stream stream)
    {
        throw new NotImplementedException();
    }

    #region private operations

    private async Task<MegaApiClient> GetClient()
    {
        if (_client == null) {
            _client = new MegaApiClient();
            await _client.LoginAsync(_username, _password);
        }

        return _client;
    }
    #endregion
}
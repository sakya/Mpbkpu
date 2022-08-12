using Dropbox.Api;
using MpbkpuLib.Interfaces;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class Dropbox : IFileManager
{
    private const string AppKey = "";

    private DropboxClient? _client;
    public void Dispose()
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }

    public Task<bool> DeleteDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        throw new NotImplementedException();
    }

    public Task<bool> DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        throw new NotImplementedException();
    }

    public Task<bool> DirectoryExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        throw new NotImplementedException();
    }

    public Task<bool> FileExists(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        throw new NotImplementedException();
    }

    public Task<File> GetFileInfo(string? path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        throw new NotImplementedException();
    }

    public IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentNullException(path);

        throw new NotImplementedException();
    }

    public Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(filePath);

        throw new NotImplementedException();
    }

    public Task<bool> CloseFile(Stream stream)
    {
        throw new NotImplementedException();
    }

    #region private operations
    private async Task<DropboxClient> GetClient()
    {
        if (_client == null) {
            _client = new DropboxClient(null, AppKey);
        }
        return _client;
    }
    #endregion
}
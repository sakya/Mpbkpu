using MpbkpuLib.Interfaces;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class Nextcloud : IFileManager
{
    public const string Scheme = "nextcloud://";

    private readonly string? _serverUri;
    private readonly string? _username;
    private readonly string? _password;

    public Nextcloud(string serverUri, string username, string password)
    {
        if (string.IsNullOrEmpty(serverUri))
            throw new ArgumentNullException(nameof(serverUri));

        _serverUri = serverUri;
        _username = username;
        _password = password;
    }

    public void Dispose()
    {
    }

    public string? GetScheme()
    {
        return Scheme;
    }

    public char GetPathSeparator()
    {
        return '/';
    }

    public bool CanOpenDatabase()
    {
        return false;
    }

    public bool SupportCompression()
    {
        return false;
    }

    public bool SupportOAuthLogin()
    {
        return true;
    }

    public string CombinePath(params string[] paths)
    {
        return Utility.Files.CombinePath(GetPathSeparator(), paths);
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

    public Task<File> GetFileInfo(string? path)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<File> GetDirectoryContent(string? path, bool includeHidden = true)
    {
        throw new NotImplementedException();
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

    #endregion
}
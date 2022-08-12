using MpbkpuLib.Interfaces;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib.FileManagers;

public class GoogleDrive : IFileManager
{
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
        throw new NotImplementedException();
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
}
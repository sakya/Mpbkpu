using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Interfaces
{
    public interface IFileManager : IDisposable
    {
        /// <summary>
        /// Return if the database can b directly opend in this file system
        /// </summary>
        /// <returns>True/False</returns>
        public bool CanOpenDatabase();
        public bool SupportCompression();
        public string CombinePath(params string[] paths);
        public Task<bool> CreateDirectory(string? path);
        public Task<bool> DeleteDirectory(string? path);
        public Task<bool> DeleteFile(string? path);
        public Task<bool> DirectoryExists(string? path);
        public Task<bool> FileExists(string? path);
        public Task<DataModel.File> GetFileInfo(string? path);
        public IAsyncEnumerable<DataModel.File> GetDirectoryContent(string? path, bool includeHidden = true);
        public Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null);
        public Task<bool> CloseFile(Stream stream);

    }
}

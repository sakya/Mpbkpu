using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Interfaces
{
    public interface IFileManager : IDisposable
    {
        /// <summary>
        /// Get the scheme of the file system
        /// </summary>
        /// <returns></returns>
        public string? GetScheme();

        /// <summary>
        /// Get the char used to separate directories in path
        /// </summary>
        /// <returns></returns>
        public char GetPathSeparator();

        /// <summary>
        /// Return if the database can b directly opend in this file system
        /// </summary>
        /// <returns>True/False</returns>
        public bool CanOpenDatabase();

        /// <summary>
        /// Return if this <see cref="IFileManager"/> supports compression
        /// </summary>
        /// <returns>True/False</returns>
        public bool SupportCompression();

        /// <summary>
        /// Combine <see cref="paths"/>
        /// </summary>
        /// <param name="paths">The paths to combine</param>
        /// <returns>The combined path</returns>
        public string CombinePath(params string[] paths);

        /// <summary>
        /// Create a directory
        /// </summary>
        /// <param name="path">The path of the directory</param>
        /// <returns>True on success</returns>
        public Task<bool> CreateDirectory(string? path);

        /// <summary>
        /// Delete a directory
        /// </summary>
        /// <param name="path">The path of the directory</param>
        /// <returns>True on success</returns>
        public Task<bool> DeleteDirectory(string? path);

        /// <summary>
        /// Delete a file
        /// </summary>
        /// <param name="path">The path of the file</param>
        /// <returns>True on success</returns>
        public Task<bool> DeleteFile(string? path);

        /// <summary>
        /// Check if a directory exists
        /// </summary>
        /// <param name="path">The path of the directory</param>
        /// <returns>True/False</returns>
        public Task<bool> DirectoryExists(string? path);

        /// <summary>
        /// Check if a file exists
        /// </summary>
        /// <param name="path">The path of the file</param>
        /// <returns>True/False</returns>
        public Task<bool> FileExists(string? path);

        /// <summary>
        /// Get information of a file
        /// </summary>
        /// <param name="path">The path of the file</param>
        /// <exception cref="FileNotFoundException">If the file does not exists</exception>
        /// <returns></returns>
        public Task<DataModel.File> GetFileInfo(string? path);

        /// <summary>
        /// Get the content of a directory
        /// </summary>
        /// <param name="path">The path of the directory</param>
        /// <param name="includeHidden">If true also hidden files are returned</param>
        /// <returns></returns>
        public IAsyncEnumerable<DataModel.File> GetDirectoryContent(string? path, bool includeHidden = true);

        /// <summary>
        /// Open a file
        /// </summary>
        /// <param name="filePath">The path of the file</param>
        /// <param name="write">If true the file is opened for write</param>
        /// <param name="size">The file size (used only if <see cref="write"/> is true)</param>
        /// <returns>A <see cref="FileSystemStream"/></returns>
        public Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null);

        /// <summary>
        /// Close a file
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/></param>
        /// <returns>True on success</returns>
        public Task<bool> CloseFile(Stream stream);
    }
}

using MpbkpuLib.FileManagers;

namespace MpbkpuLib.Interfaces
{
    public interface IFileManager : IDisposable
    {
        /// <summary>
        /// Get if the file system is case sensitive
        /// </summary>
        bool IsCaseSensitive { get; }

        /// <summary>
        /// Get the scheme of the file system
        /// </summary>
        /// <returns></returns>
        string? GetScheme();

        /// <summary>
        /// Get the char used to separate directories in path
        /// </summary>
        /// <returns></returns>
        char PathSeparator { get; }

        /// <summary>
        /// Return if the database can b directly opend in this file system
        /// </summary>
        /// <returns>True/False</returns>
        bool CanOpenDatabase { get; }

        /// <summary>
        /// Return if this <see cref="IFileManager"/> supports compression
        /// </summary>
        /// <returns>True/False</returns>
        bool SupportCompression { get; }

        /// <summary>
        /// Combine <see cref="paths"/>
        /// </summary>
        /// <param name="paths">The paths to combine</param>
        /// <returns>The combined path</returns>
        string CombinePath(params string[] paths);

        /// <summary>
        /// Create a directory
        /// </summary>
        /// <param name="path">The path of the directory</param>
        /// <returns>True on success</returns>
        Task<bool> CreateDirectory(string? path);

        /// <summary>
        /// Delete a directory
        /// </summary>
        /// <param name="path">The path of the directory</param>
        /// <returns>True on success</returns>
        Task<bool> DeleteDirectory(string? path);

        /// <summary>
        /// Delete a file
        /// </summary>
        /// <param name="path">The path of the file</param>
        /// <returns>True on success</returns>
        Task<bool> DeleteFile(string? path);

        /// <summary>
        /// Check if a directory exists
        /// </summary>
        /// <param name="path">The path of the directory</param>
        /// <returns>True/False</returns>
        Task<bool> DirectoryExists(string? path);

        /// <summary>
        /// Check if a file exists
        /// </summary>
        /// <param name="path">The path of the file</param>
        /// <returns>True/False</returns>
        Task<bool> FileExists(string? path);

        /// <summary>
        /// Get information of a file
        /// </summary>
        /// <param name="path">The path of the file</param>
        /// <exception cref="FileNotFoundException">If the file does not exists</exception>
        /// <returns></returns>
        Task<DataModel.File> GetFileInfo(string? path);

        /// <summary>
        /// Get the content of a directory
        /// </summary>
        /// <param name="path">The path of the directory</param>
        /// <param name="includeHidden">If true also hidden files are returned</param>
        /// <returns></returns>
        IAsyncEnumerable<DataModel.File> GetDirectoryContent(string? path, bool includeHidden = true);

        /// <summary>
        /// Open a file
        /// </summary>
        /// <param name="filePath">The path of the file</param>
        /// <param name="write">If true the file is opened for write</param>
        /// <param name="size">The file size (used only if <see cref="write"/> is true)</param>
        /// <returns>A <see cref="FileSystemStream"/></returns>
        Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null);

        /// <summary>
        /// Rename a file
        /// </summary>
        /// <param name="filePath">The path of the file</param>
        /// <param name="newName">The new name of the file</param>
        /// <returns></returns>
        Task RenameFile(string? filePath, string? newName);

        /// <summary>
        /// Close a file
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/></param>
        /// <returns>True on success</returns>
        Task<bool> CloseFile(Stream stream);
    }
}

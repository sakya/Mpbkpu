using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace MpbkpuLib.Abstracts
{
    public abstract class BaseLocalFileSystem : IFileManager
    {
        public void Dispose()
        {

        }

        public virtual bool IsCaseSensitive => true;

        public string? GetScheme()
        {
            return null;
        }

        public virtual char PathSeparator => Path.DirectorySeparatorChar;

        public bool CanOpenDatabase => true;

        public bool SupportCompression => true;

        public string CombinePath(params string[] paths)
        {
            return Utility.Files.CombinePath(PathSeparator, paths);
        }

        public Task<bool> CreateDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);
            Directory.CreateDirectory(path);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);
            DeleteRecursive(new DirectoryInfo(path));
            return Task.FromResult(true);
        }

        public Task<bool> DeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);
            var fi = new FileInfo(path);
            if (fi.Exists)
                ClearReadOnly(fi);
            File.Delete(path);
            return Task.FromResult(true);
        }

        public Task<bool> DirectoryExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);
            return Task.FromResult(Directory.Exists(path));
        }

        public Task<DataModel.File> GetFileInfo(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            if (!File.Exists(path))
                throw new FileNotFoundException($"File {path} not found", path);

            var fi = new FileInfo(path);
            var fInfo = new DataModel.File()
            {
                FullPath = fi.FullName,
                Name = fi.Name,
                Type = DataModel.File.FileTyes.File,
                Extension = fi.Extension,
                Size = fi.Length,

                Created = fi.CreationTimeUtc,
                LastModified = fi.LastWriteTimeUtc,
            };
            return Task.FromResult(fInfo);
        }

        public async IAsyncEnumerable<DataModel.File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            DirectoryInfo di = new DirectoryInfo(path);
            foreach (var sdi in await Task.Run( () => di.GetDirectories())) {
                // Don't follow junctions and symbolic links to directories: they can create loops.
                // Other reparse points (cloud files placeholders like OneDrive, deduplication...) are normal directories.
                if (sdi.LinkTarget != null)
                    continue;

                var fInfo = new DataModel.File()
                {
                    FullPath = sdi.FullName,
                    Name = sdi.Name,
                    Type = DataModel.File.FileTyes.Directory,

                    Created = sdi.CreationTimeUtc,
                    LastModified = sdi.LastWriteTimeUtc,
                    Hidden = IsFileHidden(sdi)
                };

                if (includeHidden || !fInfo.Hidden)
                    yield return fInfo;
            }


            foreach (var fi in await Task.Run( () => di.GetFiles())) {
                // Symbolic links to files are followed; broken links are skipped
                if (fi.LinkTarget != null && !File.Exists(fi.FullName))
                    continue;

                var fInfo = new DataModel.File()
                {
                    FullPath = fi.FullName,
                    Name = fi.Name,
                    Type = DataModel.File.FileTyes.File,
                    Extension = fi.Extension,
                    Size = fi.LinkTarget != null ? new FileInfo(fi.FullName).Length : fi.Length,

                    Created = fi.CreationTimeUtc,
                    LastModified = fi.LastWriteTimeUtc,
                    Hidden = IsFileHidden(fi)
                };

                if (includeHidden || !fInfo.Hidden)
                    yield return fInfo;
            }
        }

        public Task<bool> FileExists(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);
            return Task.FromResult(File.Exists(path));
        }

        public Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);
            Stream? res;
            if (!write)
                // Allow reading files open for writing by other processes: changes during the copy are detected by the jobs
                res = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            else
                res = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            return Task.FromResult(new FileSystemStream(this, res));
        }

        public Task RenameFile(string? filePath, string? newName)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);
            if (string.IsNullOrEmpty(newName))
                throw new ArgumentNullException(newName);
            if (newName.Contains(PathSeparator))
                throw new ArgumentException($"The new name cannot contain '{PathSeparator}'", newName);

            var path = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(path))
                throw new Exception("Invalid path");
            File.Move(filePath, Utility.Files.CombinePath(PathSeparator, path, newName));

            return Task.CompletedTask;
        }

        // OpenFile
        public Task<bool> CloseFile(Stream stream)
        {
            stream.Close();
            return Task.FromResult(true);
        }

        protected abstract bool IsFileHidden(FileSystemInfo fileInfo);

        /// <summary>
        /// Delete a directory and its content: links are removed without touching their targets
        /// (Directory.Delete fails on junctions) and read-only files are deleted too
        /// </summary>
        private static void DeleteRecursive(DirectoryInfo dir)
        {
            if (dir.LinkTarget == null) {
                foreach (var sub in dir.GetDirectories())
                    DeleteRecursive(sub);
                foreach (var file in dir.GetFiles()) {
                    ClearReadOnly(file);
                    file.Delete();
                }
            }
            dir.Delete();
        }

        private static void ClearReadOnly(FileInfo file)
        {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                file.Attributes &= ~FileAttributes.ReadOnly;
        }
    }
}

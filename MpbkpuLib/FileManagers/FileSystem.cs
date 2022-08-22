using MpbkpuLib.Interfaces;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace MpbkpuLib.FileManagers
{
    public class FileSystem : IFileManager
    {
        public void Dispose()
        {

        }

        public string? GetScheme()
        {
            return null;
        }

        public char GetPathSeparator()
        {
            return Path.DirectorySeparatorChar;
        }

        public bool CanOpenDatabase()
        {
            return true;
        }

        public bool SupportCompression()
        {
            return true;
        }

        public bool SupportOAuthLogin()
        {
            return false;
        }

        public string CombinePath(params string[] paths)
        {
            return Utility.Files.CombinePath(GetPathSeparator(), paths);
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
            Directory.Delete(path, true);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteFile(string? path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);
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

                Created = fi.CreationTime,
                LastModified = fi.LastWriteTime,
            };
            return Task.FromResult(fInfo);
        }

        public async IAsyncEnumerable<DataModel.File> GetDirectoryContent(string? path, bool includeHidden = true)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(path);

            DirectoryInfo di = new DirectoryInfo(path);
            foreach (var sdi in await Task.Run( () => di.GetDirectories())) {
                if (sdi.Name == ".." || sdi.Name == ".")
                    continue;

                var fInfo = new DataModel.File()
                {
                    FullPath = sdi.FullName,
                    Name = sdi.Name,
                    Type = DataModel.File.FileTyes.Directory,

                    Created = di.CreationTime,
                    LastModified = di.LastWriteTime,
                };

                if (Environment.OSVersion.Platform == PlatformID.Unix && fInfo.Name.StartsWith("."))
                    fInfo.Hidden = true;
                else
                    fInfo.Hidden = sdi.Attributes.HasFlag(FileAttributes.Hidden);

                if (includeHidden || !fInfo.Hidden)
                    yield return fInfo;
            }


            foreach (var fi in await Task.Run( () => di.GetFiles())) {
                var fInfo = new DataModel.File()
                {
                    FullPath = fi.FullName,
                    Name = fi.Name,
                    Type = DataModel.File.FileTyes.File,
                    Extension = fi.Extension,
                    Size = fi.Length,

                    Created = fi.CreationTime,
                    LastModified = fi.LastWriteTime,
                };

                if (Environment.OSVersion.Platform == PlatformID.Unix && fInfo.Name.StartsWith("."))
                    fInfo.Hidden = true;
                else
                    fInfo.Hidden = fi.Attributes.HasFlag(FileAttributes.Hidden);

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
                res = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            else
                res = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            return Task.FromResult(new FileSystemStream(this, res));
        } // OpenFile

        public Task<bool> CloseFile(Stream stream)
        {
            stream.Close();
            return Task.FromResult(true);
        }
    }
}

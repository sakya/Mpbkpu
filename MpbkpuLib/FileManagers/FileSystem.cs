using MpbkpuLib.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MpbkpuLib.FileManagers
{
    public class FileSystem : IFileManager
    {
        public void Dispose()
        {

        }

        public bool CanOpenDatabase()
        {
            return true;
        }

        public string CombinePath(string? part1, string part2)
        {
            if (string.IsNullOrEmpty(part1))
                throw new ArgumentNullException(nameof(part1));
            if (string.IsNullOrEmpty(part2))
                throw new ArgumentNullException(nameof(part2));
            return Path.Combine(part1, part2);
        }

        public string CombinePath(string? part1, string part2, string part3)
        {
            if (string.IsNullOrEmpty(part1))
                throw new ArgumentNullException(nameof(part1));
            if (string.IsNullOrEmpty(part2))
                throw new ArgumentNullException(nameof(part2));
            if (string.IsNullOrEmpty(part3))
                throw new ArgumentNullException(nameof(part3));
            return Path.Combine(part1, part2, part3);
        }

        public string CombinePath(string? part1, string part2, string part3, string part4)
        {
            if (string.IsNullOrEmpty(part1))
                throw new ArgumentNullException(nameof(part1));
            if (string.IsNullOrEmpty(part2))
                throw new ArgumentNullException(nameof(part2));
            if (string.IsNullOrEmpty(part3))
                throw new ArgumentNullException(nameof(part3));
            if (string.IsNullOrEmpty(part4))
                throw new ArgumentNullException(nameof(part4));

            return Path.Combine(part1, part2, part3, part4);
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

        public Task<Stream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(filePath);
            Stream? res = null;
            if (!write)
                res = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            else
                res = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            return Task.FromResult(res);
        } // OpenFile
    }
}

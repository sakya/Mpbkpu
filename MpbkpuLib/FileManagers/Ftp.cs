using MpbkpuLib.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentFTP;
using System.Net;

namespace MpbkpuLib.FileManagers
{
    public class Ftp : IFileManager
    {
        public string? Host { get; set; }
        public int Port { get; set; }
        public NetworkCredential? Credentials { get; set; }

        public string CombinePath(string? part1, string part2)
        {
            if (string.IsNullOrEmpty(part1))
                throw new ArgumentNullException(nameof(part1));
            if (string.IsNullOrEmpty(part2))
                throw new ArgumentNullException(nameof(part2));

            if (!part1.EndsWith(("/")))
                part1 = $"{part1}/";
            return $"{part1}{part2}";
        }

        public IAsyncEnumerable<DataModel.File> GetDirectoryContent(string? position, bool includeHidden = true)
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

        public Task<Stream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            throw new NotImplementedException();
        }
    }
}
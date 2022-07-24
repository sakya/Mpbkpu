using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MpbkpuLib.Interfaces
{
    public interface IFileManager
    {
        public string CombinePath(string? part1, string part2);
        public Task<bool> CreateDirectory(string? path);
        public Task<bool> DeleteDirectory(string? path);
        public Task<bool> DeleteFile(string? path);
        public Task<bool> DirectoryExists(string? path);
        public Task<bool> FileExists(string? path);
        public IAsyncEnumerable<DataModel.File> GetDirectoryContent(string? position, bool includeHidden = true);
        public Task<Stream> OpenFile(string? filePath, bool write = false, long? size = null);

    }
}

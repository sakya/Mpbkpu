using MpbkpuLib.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MpbkpuLib.FileManagers
{
    public class Smb : IFileManager
    {
        private string? _domainName = null;
        private string? _username = null;
        private string? _password = null;
        private string? _host = null;

        public Smb(string domainName, string username, string password, string host)
        {
            _domainName = domainName;
            _username = username;
            _password = password;
            _host = host;
        }

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
        
        public string? Host
        {
            get { return _host; }
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
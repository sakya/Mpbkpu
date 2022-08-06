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
    public class OneDrive : IFileManager
    {
        private string? _accessToken = null;
        private string? _refreshToken = null;

        public void Dispose()
        {

        }

        public bool CanOpenDatabase()
        {
            return false;
        }

        public OneDrive(string? accessToken, string? refreshToken)
        {
            _accessToken = accessToken;
            _refreshToken = refreshToken;
        }

        public string CombinePath(params string[] paths)
        {
            string res = string.Empty;
            foreach (var p in paths) {
                if (string.IsNullOrEmpty(p))
                    throw new Exception("Path cannot be empty");

                var part = p;
                if (!part.EndsWith("/"))
                    part = $"{part}/";
                res = $"{res}{part}";
            }

            return res;
        }

        public IAsyncEnumerable<DataModel.File> GetDirectoryContent(string? path, bool includeHidden = true)
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

        public Task<FileSystemStream> OpenFile(string? filePath, bool write = false, long? size = null)
        {
            throw new NotImplementedException();
        }

        public Task<bool> CloseFile(Stream stream)
        {
            throw new NotImplementedException();
        }
    }
}
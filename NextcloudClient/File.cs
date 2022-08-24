using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using NextcloudClient.Exceptions;
using NextcloudClient.Models;

namespace NextcloudClient
{
    public static class File
    {
        /// <summary>
        /// Get file info
        /// </summary>
        /// <param name="client">The <see cref="Client"/></param>
        /// <param name="path">The file path</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<ItemInfo> GetInfo(Client client, string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!path.StartsWith("/"))
                path = $"/{path}";

            var xdoc = XDocument.Parse(Client.PropfindDoc);
            var url = $"{client.ServerUri}remote.php/dav/files/{client.UserId}{path}";

            try {
                var res =  await client.CallApi<ItemInfo>(new HttpMethod("PROPFIND"), new Uri(url), xdoc, new Dictionary<string, string>()
                {
                    { "Depth", "0"}
                });
                if (res.Response.Count != 1 || res.Response[0].PropStat.Properties.ContainedFileCount != null)
                    throw new FileNotFoundException($"File {path} not found");
                return res;
            } catch (ApiException ex) {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    throw new FileNotFoundException($"File {path} not found");
                throw;
            }
        }

        /// <summary>
        /// Delete a file
        /// </summary>
        /// <param name="client">The <see cref="Client"/></param>
        /// <param name="path">The path of folder</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task Delete(Client client, string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!path.StartsWith("/"))
                path = $"/{path}";

            await GetInfo(client, path);
            var url = $"{client.ServerUri}remote.php/dav/files/{client.UserId}{path}";
            await client.CallApi<string>(new HttpMethod("DELETE"), new Uri(url), null);
        }

        /// <summary>
        /// Get a <see cref="Stream"/> to download a file
        /// </summary>
        /// <param name="client">The <see cref="Client"/></param>
        /// <param name="path">The path of folder</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<Stream> Download(Client client, string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!path.StartsWith("/"))
                path = $"/{path}";

            await GetInfo(client, path);
            var url = $"{client.ServerUri}remote.php/dav/files/{client.UserId}{path}";
            return await client.GetDownloadStream(new Uri(url));
        }

        public static async Task Upload(Client client, string path, Stream stream)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!path.StartsWith("/"))
                path = $"/{path}";

            var url = $"{client.ServerUri}remote.php/dav/files/{client.UserId}{path}";
            await client.UploadStream(new Uri(url), stream);
        }
    }
}
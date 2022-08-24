using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using NextcloudClient.Exceptions;
using NextcloudClient.Models;

namespace NextcloudClient
{
    public static class Folder
    {
        /// <summary>
        /// Get folder info
        /// </summary>
        /// <param name="client">The <see cref="Client"/></param>
        /// <param name="path">The folder path</param>
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
                var res = await client.CallApi<ItemInfo>(new HttpMethod("PROPFIND"), new Uri(url), xdoc,
                    new Dictionary<string, string>()
                    {
                        { "Depth", "0" }
                    });
                if (res.Response.Count != 1 || res.Response[0].PropStat.Properties.ResourceType.Collection == null)
                    throw new DirectoryNotFoundException($"Folder {path} not found");
                return res;
            } catch (ApiException ex) {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                    throw new DirectoryNotFoundException($"Folder {path} not found");
                throw;
            }
        }

        /// <summary>
        /// List content of a folder
        /// </summary>
        /// <param name="client">The <see cref="Client"/></param>
        /// <param name="path">The folder path</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task<ItemInfo> ListContent(Client client, string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!path.StartsWith("/"))
                path = $"/{path}";

            var xdoc = XDocument.Parse(Client.PropfindDoc);
            var url = $"{client.ServerUri}remote.php/dav/files/{client.UserId}{path}";
            return await client.CallApi<ItemInfo>(new HttpMethod("PROPFIND"), new Uri(url), xdoc);
        }

        /// <summary>
        /// Create a new folder
        /// </summary>
        /// <param name="client">The <see cref="Client"/></param>
        /// <param name="path">The path of the new folder</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static async Task Create(Client client, string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!path.StartsWith("/"))
                path = $"/{path}";

            var url = $"{client.ServerUri}remote.php/dav/files/{client.UserId}{path}";
            await client.CallApi<string>(new HttpMethod("MKCOL"), new Uri(url), null);
        }

        /// <summary>
        /// Delete a folder
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
    }
}
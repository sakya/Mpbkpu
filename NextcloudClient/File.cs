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
            var url = client.GetDavUrl(path);

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
            var url = client.GetDavUrl(path);
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
            var url = client.GetDavUrl(path);
            return await client.GetDownloadStream(new Uri(url));
        }

        /// <summary>
        /// Move a file
        /// </summary>
        /// <param name="client">The <see cref="Client"/></param>
        /// <param name="sourcePath">The source path</param>
        /// <param name="destinationPath">The destination path</param>
        /// <param name="overwrite">If true the destination file is overwritten if it exists</param>
        public static async Task Move(Client client, string sourcePath, string destinationPath, bool overwrite)
        {
            if (string.IsNullOrEmpty(sourcePath))
                throw new ArgumentNullException(nameof(sourcePath));
            if (string.IsNullOrEmpty(destinationPath))
                throw new ArgumentNullException(nameof(destinationPath));

            if (!sourcePath.StartsWith("/"))
                sourcePath = $"/{sourcePath}";
            if (!destinationPath.StartsWith("/"))
                destinationPath = $"/{destinationPath}";

            var url = client.GetDavUrl(sourcePath);
            await client.CallApi<string>(new HttpMethod("MOVE"), new Uri(url), null,
                new Dictionary<string, string>()
                {
                    { "Destination", client.GetDavUrl(destinationPath) },
                    { "Overwrite", overwrite ? "T" : "F" },
                });
        }

        /// <summary>
        /// Upload a <see cref="Stream"/>
        /// </summary>
        /// <param name="client">The <see cref="Client"/></param>
        /// <param name="path">The path to upload the file</param>
        /// <param name="stream">The <see cref="Stream"/></param>
        /// <param name="length">The number of bytes to upload</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <param name="length">The length of the data, null when unknown (chunked transfer encoding)</param>
        public static async Task Upload(Client client, string path, Stream stream, long? length)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));
            if (!path.StartsWith("/"))
                path = $"/{path}";

            var url = client.GetDavUrl(path);
            await client.UploadStream(new Uri(url), stream, length);
        }
    }
}
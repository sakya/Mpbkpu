using System.Security.Cryptography;
using MpbkpuLib.Interfaces;

namespace MpbkpuLib.Utility;

public static class Files
{
    /// <summary>
    /// Copy a file
    /// </summary>
    /// <param name="source">The source <see cref="Stream"/></param>
    /// <param name="destination">The destination <see cref="Stream"/></param>
    /// <returns>True on success</returns>
    public static async Task<bool> Copy(Stream source, Stream destination)
    {
        byte[] buffer = new byte[32768];
        int read;
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length)) > 0) {
            await destination.WriteAsync(buffer, 0, read);
        }

        return true;
    }

    public static string CombinePath(char separator, params string[] paths)
    {
        string res = string.Empty;
        foreach (var p in paths) {
            if (string.IsNullOrEmpty(p))
                throw new Exception("Path cannot be empty");

            if (res.EndsWith($"{separator}") && p.StartsWith($"{separator}"))
                res = res.Remove(res.Length - 1);
            res = !string.IsNullOrEmpty(res) && !res.EndsWith($"{separator}") && !p.StartsWith($"{separator}") ? $"{res}{separator}{p}" : $"{res}{p}";
        }

        return res;
    }

    public static string GetPath(char separator, string scheme, string path)
    {
        if (path.StartsWith(scheme)) {
            var res = path.Remove(0, scheme.Length);
            if (!res.StartsWith($"{separator}"))
                res = $"{separator}{res}";
            return res;
        }

        return path;
    }

    public static string GetParent(char separator, string path, out string? childName)
    {
        childName = null;
        if (path.EndsWith(separator))
            path = path.Remove(path.Length - 1, 1);
        if (!path.StartsWith(separator))
            path = $"{separator}{path}";

        int idx = path.LastIndexOf($"{separator}", StringComparison.InvariantCultureIgnoreCase);
        if (idx >= 0) {
            childName = path.Substring(idx + 1);
            return path.Substring(0, idx);
        }

        throw new Exception($"Cannot get parent of {path}");
    }

    public static long? GetDestinationFileSize(long sourceFileSize, bool compress, bool encrypt)
    {
        long? destFileSize = sourceFileSize;
        if (compress) {
            // Unknown file size
            destFileSize = null;
        } else if (encrypt) {
            destFileSize = (sourceFileSize / 16 + 1) * 16;
            // Header
            destFileSize += 32;
        }

        return destFileSize;
    }

    public static async Task<string> GetMD5(IFileManager? fm, string? filePath)
    {
        if (fm == null)
            throw new ArgumentNullException(nameof(fm));
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(nameof(filePath));

        using (var source = await fm.OpenFile(filePath)) {
            using (var hasher = MD5.Create()) {
                var hash = await hasher.ComputeHashAsync(source);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
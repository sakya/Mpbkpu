using System.IO.Compression;
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
    /// <param name="progressAction">A <see cref="Action{T}"/> called to report progress</param>
    /// <param name="token">The <see cref="CancellationToken"/></param>
    /// <returns>True on success</returns>
    public static async Task Copy(Stream source, Stream destination, Action<long>? progressAction = null, CancellationToken? token = null)
    {
        var buffer = new byte[32768];
        int read;
        long copied = 0;
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length)) > 0) {
            token?.ThrowIfCancellationRequested();
            await destination.WriteAsync(buffer, 0, read);

            copied += read;
            progressAction?.Invoke(copied);
        }
    }

    /// <summary>
    /// Compare the current modification time of a file (UTC) with the one saved in a backup database.
    /// Backups made by older versions saved local times: they match too, so files are not backed up again.
    /// </summary>
    public static bool SameModificationTime(DateTime? current, DateTime? saved)
    {
        if (current == saved)
            return true;
        if (current == null || saved == null)
            return false;
        return DateTime.SpecifyKind(saved.Value, DateTimeKind.Local).ToUniversalTime().Ticks == current.Value.Ticks;
    }

    public static string CombinePath(char separator, params string[] paths)
    {
        var res = string.Empty;
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

    public static string GetParent(char separator, string path, out string? childName, string? scheme = null, bool addStartingSeparator = true)
    {
        childName = null;
        if (path.EndsWith(separator))
            path = path.Remove(path.Length - 1, 1);
        if (string.IsNullOrEmpty(scheme) || !path.StartsWith(scheme, StringComparison.InvariantCultureIgnoreCase)) {
            if (addStartingSeparator && !path.StartsWith(separator))
                path = $"{separator}{path}";
        }

        var idx = path.LastIndexOf($"{separator}", StringComparison.InvariantCultureIgnoreCase);
        if (idx >= 0) {
            childName = path.Substring(idx + 1);
            return path.Substring(0, idx);
        }

        if (!addStartingSeparator)
            return string.Empty;

        throw new Exception($"Cannot get parent of {path}");
    }

    /// <summary>
    /// Check if <see cref="path"/> is the same as <see cref="parent"/> or inside it.
    /// Local paths are compared as full paths (case insensitive on Windows and macOS), remote paths (scheme://) as they are
    /// </summary>
    public static bool IsSameOrSubPath(string parent, string path)
    {
        var local = !parent.Contains("://") && !path.Contains("://");
        var separator = '/';
        var comparison = StringComparison.Ordinal;
        if (local) {
            parent = System.IO.Path.GetFullPath(parent);
            path = System.IO.Path.GetFullPath(path);
            separator = System.IO.Path.DirectorySeparatorChar;
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                comparison = StringComparison.OrdinalIgnoreCase;
        }

        parent = $"{parent.TrimEnd(separator)}{separator}";
        path = $"{path.TrimEnd(separator)}{separator}";
        return path.StartsWith(parent, comparison);
    }

    public static long? GetDestinationFileSize(long sourceFileSize, CompressionLevel compressionLevel, bool encrypt)
    {
        long? destFileSize = sourceFileSize;
        if (compressionLevel != CompressionLevel.NoCompression) {
            // Unknown file size
            destFileSize = null;
        } else if (encrypt) {
            destFileSize = (sourceFileSize / EncryptDecrypt.AesBlockByteSize + 1) * EncryptDecrypt.AesBlockByteSize;
            destFileSize += EncryptDecrypt.AesBlockByteSize; // iv
            destFileSize += EncryptDecrypt.PasswordSaltByteSize;
            destFileSize += EncryptDecrypt.HeaderSize;
            destFileSize += EncryptDecrypt.MacByteSize;
        }

        return destFileSize;
    }

    public static async Task<string> GetSha256(IFileManager? fm, string? filePath, Action<long>? progressAction = null, CancellationToken? token = null)
    {
        if (fm == null)
            throw new ArgumentNullException(nameof(fm));
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentNullException(nameof(filePath));

        await using var source = await fm.OpenFile(filePath);
        using var hasher = SHA256.Create();
        int read;
        var processed = 0;
        var buffer = new byte[32768];
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length)) > 0) {
            token?.ThrowIfCancellationRequested();
            hasher.TransformBlock(buffer, 0, read, null, 0);

            processed += read;
            progressAction?.Invoke(processed);
        }
        hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return BitConverter.ToString(hasher.Hash!).Replace("-", "").ToLowerInvariant();
    }
}
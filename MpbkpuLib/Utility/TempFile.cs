namespace MpbkpuLib.Utility;

public static class TempFile
{
    public static string? GetTempFileName(string? extension = null)
    {
        string? res = null;
        if (!string.IsNullOrEmpty(extension) && !extension.StartsWith("."))
            extension = $".{extension}";

        var tempPath = Path.GetTempPath();
        while (res == null) {
            res = Path.Combine(tempPath, $"{Guid.NewGuid().ToString("N")}{extension}");
            if (!File.Exists(res)) {
                try {
                    using (var _ = new FileStream(res, FileMode.Create, FileAccess.Write, FileShare.Read)) {
                    }
                    return res;
                } catch {
                    res = null;
                }
            } else {
                res = null;
            }
        }

        return null;
    }

    public static Stream? GetTempFileStream(string? extension, out string? tempFilePath)
    {
        tempFilePath = GetTempFileName(extension);
        if (!string.IsNullOrEmpty(tempFilePath))
            return new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        return null;
    }
}
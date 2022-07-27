namespace MpbkpuLib.Utility;

public static class TempFile
{
    public static Stream? GetTempFileStream(string? extension, out string? tempFilePath)
    {
        tempFilePath = null;
        var tempFolder = Path.GetTempPath();
        while (tempFilePath == null) {
            var fileName = $"{Guid.NewGuid().ToString("N")}{extension}";
            tempFilePath = Path.Combine(tempFolder, fileName);
            if (!File.Exists(fileName)) {
                try {
                    return new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                } catch {
                    tempFilePath = null;
                }
            }
        }

        return null;
    }
}
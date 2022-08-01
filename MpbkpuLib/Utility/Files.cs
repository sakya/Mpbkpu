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
        byte[] buffer = new byte[4096];
        int read;
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length)) > 0) {
            await destination.WriteAsync(buffer, 0, read);
        }

        return true;
    }
}
using System.Diagnostics;

namespace MpbkpuLib.Utility;

public static class Browser
{
    /// <summary>
    /// Open an url with the default browser
    /// </summary>
    /// <param name="url">The url</param>
    /// <returns>The <see cref="Process"/></returns>
    public static void Open(string url)
    {
        using var process = new Process();
        var startInfo = new ProcessStartInfo()
        {
            CreateNoWindow = true
        };
        process.StartInfo = startInfo;
        if (OperatingSystem.IsLinux()) {
            startInfo.FileName = "xdg-open";
            startInfo.ArgumentList.Add(url);
        } else if (OperatingSystem.IsMacOS()) {
            startInfo.FileName = "open";
            startInfo.ArgumentList.Add(url);
        } else if (OperatingSystem.IsWindows()) {
            startInfo.FileName = "cmd";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("start");
            startInfo.ArgumentList.Add(url.Replace("&", "^&"));
        } else {
            throw new Exception("Unsupported OS");
        }
        process.Start();
        process.WaitForExit();
    }
}
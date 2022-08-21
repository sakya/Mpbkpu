namespace MpbkpuLib.Utility;

public static class String
{
    public static string BytesToString(long size)
    {
        if (size < 0)
            size = 0;

        if (size / (1024.0 * 1024.0 * 1024.0) > 0.5)
            return Math.Round(size / (1024.0 * 1024.0 * 1024.0), 1).ToString("0.# Gib");
        if (size / (1024.0 * 1024.0) > 0.5)
            return Math.Round(size / (1024.0 * 1024.0), 1).ToString("0.# Mib");
        if (size / 1024.0 > 0.5)
            return Math.Round(size / 1024.0, 1).ToString("0.# Kib");
        return size.ToString("0 bytes");
    }

    public static string TimeSpanToString(TimeSpan ts)
    {
        return ts.ToString(ts.Days > 0 ? @"d\.hh\:mm\:ss" : @"hh\:mm\:ss");
    }
}
using System;
using System.IO;

namespace Tests;

public class BaseTest
{
    protected const string BackupSource = "/home/sakya/Mpbkpu_Test/source";
    protected const string BackupDestination = "/home/sakya/Mpbkpu_Test/backup";
    protected const string RestoreDestination = "/home/sakya/Mpbkpu_Test/restore";
    protected const string EncryptionPassword = "password";
    private readonly Random _random = new();

    protected void PrepareSourceDirectory()
    {
        // Prepare files in the source directory
        if (Directory.Exists(BackupSource))
            Directory.Delete(BackupSource, true);
        if (Directory.Exists(BackupDestination))
            Directory.Delete(BackupDestination, true);
        if (Directory.Exists(RestoreDestination))
            Directory.Delete(RestoreDestination, true);

        Directory.CreateDirectory(BackupSource);
        Directory.CreateDirectory(Path.Combine(BackupSource, "SubDirectory1"));
        Directory.CreateDirectory(Path.Combine(BackupSource, "SubDirectory2"));
        Directory.CreateDirectory(BackupDestination);
        Directory.CreateDirectory(RestoreDestination);

        var minSize = 64 * 1024;
        var maxSize = 5 * 1024 * 1024;

        for (var i = 0; i < _random.Next(3, 10); i++) {
            CreateFile(Path.Combine(BackupSource, $"file{i}"), _random.Next(minSize, maxSize));
        }

        for (var i = 0; i < _random.Next(3, 10); i++) {
            CreateFile(Path.Combine(BackupSource, "SubDirectory1", $"file{i}"), _random.Next(minSize, maxSize));
        }

        for (var i = 0; i < _random.Next(3, 10); i++) {
            CreateFile(Path.Combine(BackupSource, "SubDirectory2", $"file{i}"), _random.Next(minSize, maxSize));
        }
    }

    protected void CleanupDirectories()
    {
        // Delete BackupSource and BackupDestination
        if (Directory.Exists(BackupSource))
            Directory.Delete(BackupSource, true);
        if (Directory.Exists(BackupDestination))
            Directory.Delete(BackupDestination, true);
        if (Directory.Exists(RestoreDestination))
            Directory.Delete(RestoreDestination, true);        
    }
    
    private void CreateFile(string path, long size)
    {
        long write = 0;
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) {
            while (write < size) {
                var buffer = new byte[4096];
                int toWrite = buffer.Length;
                if (size - write < toWrite)
                    toWrite = (int)(size - write);

                for (var i = 0; i < toWrite; i++) {
                    buffer[i] = (byte)_random.Next(0, 255);
                }
                fs.Write(buffer, 0, toWrite);
                write += toWrite;
            }
        }
    }
}
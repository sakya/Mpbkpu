using System.Reflection;
using Azure;
using Heyes;

namespace Mpbkpu;

public static class Program
{
    public static void PrintInfo()
    {
        Console.WriteLine($"Multi Platform Backup Utility v{Assembly.GetExecutingAssembly().GetName().Version?.ToString()}");
        Console.WriteLine();
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("mpbkpu ACTION [OPTIONS]");
        
        Console.WriteLine();
        Console.WriteLine("Actions:");
        Console.WriteLine("  --backup             backup a source folder to a destination");
        Console.WriteLine("  --restore            restore a backup to a destination");
        Console.WriteLine("  --delete             delete an existing backup");

        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --source=SOURCE      source path of the backup");
        Console.WriteLine("  --dest=DEST          destination path of the backup");
        Console.WriteLine("  --password=PWD       password to encrypt/decrypt the files");
        Console.WriteLine("  --compress           enable file compression");
        
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("mpbkpu --backup --source=/home/user/ --dest=/mnt/backup/");
    }
    
    public static int Main(string[] args)
    {
        PrintInfo();
        if (args.Length == 0) {
            PrintHelp();
            return -1;
        }

        GetOpt pArgs = new GetOpt(args);
        try {
            pArgs.SetOpts(new [] { "backup", "restore", "delete", "source=", "dest=", "compress", "password="});
            pArgs.Parse();
        } catch (ArgumentException e) {
            Console.WriteLine(e.Message);
            Console.WriteLine();
            PrintHelp();
            return -1;
        }
        
        foreach (var arg in pArgs.Args)
            Console.WriteLine(arg);

        // Check arguments
        if (string.IsNullOrEmpty(pArgs.GetOptionArg("source"))) {
            Console.WriteLine("Missing source");
            return 1;
        }
            
        if (!pArgs.IsDefined("dest") || string.IsNullOrEmpty(pArgs.GetOptionArg("dest"))) {
            Console.WriteLine("Missing destination");
            return 1;
        }        
        
        return 0;
    }
}

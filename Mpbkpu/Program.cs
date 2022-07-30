using System.Reflection;
using Azure;
using CmdLineArgsParser;

namespace Mpbkpu;

public static class Program
{
    private static void PrintInfo()
    {
        Console.WriteLine($"Multi Platform Backup Utility v{Assembly.GetExecutingAssembly().GetName().Version?.ToString()}");
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        var parser = new Parser();
        parser.ShowUsage<Arguments>();

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

        var pArgs = new Parser().Parse<Arguments>(args, out var errors);
        if (errors.Count > 0) {
            foreach (var e in errors) {
                Console.WriteLine(e.Message);
            }
            Console.WriteLine();
            PrintHelp();
            return -1;
        }

        return 0;
    }
}

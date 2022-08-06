using System.Reflection;
using CmdLineArgsParser;
using Microsoft.Graph;
using Mpbkpu.Actions;
using MpbkpuLib.Utility;

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
        Console.WriteLine("mpbkpu backup --source /home/user/ --dest /mnt/backup/");
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

        // Ask for password
        if (pArgs.InteractivePassword == true) {
            Console.Write(("Password: "));
            pArgs.Password = Utility.Console.AskPassword();
        }

        IAction? action = null;
        switch (pArgs.Action) {
            case Arguments.Actions.Backup:
                action = new BackupAction();
                break;
            case Arguments.Actions.Delete:
                action = new DeleteAction();
                break;
            case Arguments.Actions.List:
                action = new ListAction();
                break;
            case Arguments.Actions.Restore:
                action = new RestoreAction();
                break;
            case Arguments.Actions.Verify:
                action = new VerifyAction();
                break;
        }

        if (action != null) {
            try {
                action.Run(pArgs).Wait();
            } finally {
                Console.ResetColor();
            }
        } else {
            throw new Exception("Action non initialized");
        }

        return 0;
    }
}

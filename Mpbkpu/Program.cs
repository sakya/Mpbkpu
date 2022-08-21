using System.Text;
using CmdLineArgsParser;
using Mpbkpu.Actions;

namespace Mpbkpu;

public static class Program
{
    private static void PrintInfo()
    {
        var parser = new Parser();
        parser.ShowInfo(false);
    }

    private static void PrintHelp(bool showInfo = true)
    {
        var parser = new Parser();
        if (showInfo)
            parser.ShowInfo();
        parser.ShowUsage<Arguments>();

        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("mpbkpu backup -t Incremental -s /home/user/ -d /mnt/backup/ -c Optimal -p PWD");
        Console.WriteLine("mpbkpu list -b /mnt/backup/ -p PWD");
        Console.WriteLine("mpbkpu verify -b /mnt/backup/ -g GUID -p PWD");
        Console.WriteLine("mpbkpu restore -b /mnt/backup/ -d /home/user/ -g GUID -p PWD");
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.ForegroundColor = BaseAction.DefaultColor;
        if (args.Length == 0) {
            PrintHelp();
            return -1;
        }

        var pArgs = new Parser().Parse<Arguments>(args, out var errors);
        if (errors.Count > 0) {
            PrintInfo();
            Console.ForegroundColor = BaseAction.ErrorColor;
            foreach (var e in errors) {
                Console.WriteLine(e.Message);
            }
            Console.ForegroundColor = BaseAction.DefaultColor;
            Console.WriteLine();
            PrintHelp(false);
            return -1;
        }

        Console.CancelKeyPress += delegate
        {
            Console.CursorVisible = true;
            Console.ResetColor();
        };
        PrintInfo();
        // Ask for password
        if (pArgs.InteractivePassword) {
            Console.Write(("Password: "));
            pArgs.Password = Utility.Console.AskPassword(true);
            if (string.IsNullOrEmpty(pArgs.Password)) {
                Console.WriteLine("Empty password is not allowed, aborting");
                return -1;
            }
        }

        IAction? action = pArgs.Action switch
        {
            Arguments.Actions.Backup => new BackupAction(),
            Arguments.Actions.Delete => new DeleteAction(),
            Arguments.Actions.List => new ListAction(),
            Arguments.Actions.ListContent => new ListContentAction(),
            Arguments.Actions.Restore => new RestoreAction(),
            Arguments.Actions.Verify => new VerifyAction(),
            _ => null
        };

        if (action != null) {
            try {
                action.Run(pArgs).Wait();
                if (action.Completed.HasValue && action.Started.HasValue) {
                    Console.Write($"Completed in { (action.Completed.Value - action.Started.Value) }");
                    if (action.Bytes.HasValue)
                        Console.Write($" ({MpbkpuLib.Utility.String.BytesToString(action.Bytes.Value)})");
                    Console.WriteLine();
                }
            } finally {
                Console.ResetColor();
            }
        } else {
            throw new Exception("Action not initialized");
        }

        return 0;
    }
}

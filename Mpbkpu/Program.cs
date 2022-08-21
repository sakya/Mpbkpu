using System.Reflection;
using System.Text;
using CmdLineArgsParser;
using Mpbkpu.Actions;

namespace Mpbkpu;

public static class Program
{
    private static void PrintInfo()
    {
        var ea = Assembly.GetExecutingAssembly();
        Console.WriteLine($"Multi Platform Backup Utility v{ea.GetName().Version?.ToString()}");
        if (ea.GetCustomAttribute(typeof(AssemblyCopyrightAttribute)) is AssemblyCopyrightAttribute ca)
            Console.WriteLine(ca.Copyright);
        Console.WriteLine();
    }

    private static void PrintHelp()
    {
        var parser = new Parser();
        parser.ShowUsage<Arguments>();

        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("mpbkpu backup --source /home/user/ --destination /mnt/backup/");
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
            foreach (var e in errors) {
                Console.WriteLine(e.Message);
            }
            Console.WriteLine();
            PrintHelp();
            return -1;
        }

        Console.CancelKeyPress += delegate
        {
            Console.CursorVisible = true;
            Console.ResetColor();
        };
        PrintInfo();
        // Ask for password
        if (pArgs.InteractivePassword == true) {
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

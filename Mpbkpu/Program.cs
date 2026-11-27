using System.Text;
using CmdLineArgsParser;
using Mpbkpu.Actions;

namespace Mpbkpu;

public static class Program
{
    // Exit codes
    private const int Success = 0;
    private const int Failed = 1;
    private const int Cancelled = 2;
    private const int InvalidArguments = -1;

    private static IAction? _runningAction;

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
        // The parser formats the usage on the console width, unknown when the output is redirected
        if (Utility.Console.IsInteractive)
            parser.ShowUsage<Arguments>();
        else
            Console.WriteLine("Run mpbkpu without redirecting the output for the list of the options.");

        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("mpbkpu backup -t Incremental -s /home/user/ -d /mnt/backup/ -c Optimal -p PWD");
        Console.WriteLine("mpbkpu list -b /mnt/backup/ -p PWD");
        Console.WriteLine("mpbkpu verify -b /mnt/backup/ -g GUID -p PWD");
        Console.WriteLine("mpbkpu restore -b /mnt/backup/ -d /home/user/ -g GUID -p PWD");
        Console.WriteLine();
        Console.WriteLine($"Exit codes: {Success} success (also with warnings), {Failed} error, {Cancelled} cancelled, " +
                          $"{InvalidArguments} invalid arguments");
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.ForegroundColor = Utility.Console.DefaultColor;
        if (args.Length == 0) {
            PrintHelp();
            return InvalidArguments;
        }

        var pArgs = new Parser().Parse<Arguments>(args, out var errors);
        var messages = errors.Select(e => e.Message).ToList();
        if (messages.Count == 0 && pArgs.Validate() is { } error)
            messages.Add(error);
        if (messages.Count > 0) {
            PrintInfo();
            Console.ForegroundColor = Utility.Console.ErrorColor;
            foreach (var message in messages) {
                Console.WriteLine(message);
            }
            Console.ForegroundColor = Utility.Console.DefaultColor;
            Console.WriteLine();
            PrintHelp(false);
            return InvalidArguments;
        }

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = _runningAction?.Cancel() ?? false;
            if (!e.Cancel) {
                Utility.Console.SetCursorVisible(true);
                Console.ResetColor();
            }
        };

        PrintInfo();
        // Ask for password
        if (pArgs.InteractivePassword) {
            Console.Write(("Password: "));
            pArgs.Password = Utility.Console.AskPassword(true);
            if (string.IsNullOrEmpty(pArgs.Password)) {
                Console.WriteLine("Empty password is not allowed, aborting");
                return InvalidArguments;
            }
        }

        _runningAction = pArgs.Action switch
        {
            Arguments.Actions.Backup => new BackupAction(),
            Arguments.Actions.Delete => new DeleteAction(),
            Arguments.Actions.List => new ListAction(),
            Arguments.Actions.ListContent => new ListContentAction(),
            Arguments.Actions.Restore => new RestoreAction(),
            Arguments.Actions.Verify => new VerifyAction(),
            Arguments.Actions.SyncDirectory => new SyncAction(),
            _ => null
        };

        if (_runningAction == null)
            throw new Exception("Action not initialized");

        bool succeeded;
        try {
            succeeded = _runningAction.Run(pArgs).GetAwaiter().GetResult();
            if (_runningAction.UserCancelled()) {
                Console.ForegroundColor = Utility.Console.WarningColor;
                Console.WriteLine("Cancelled by user");
                Console.ForegroundColor = Utility.Console.DefaultColor;
            }

            if (_runningAction.Completed.HasValue && _runningAction.Started.HasValue) {
                Console.Write($"Completed in { MpbkpuLib.Utility.String.TimeSpanToString(_runningAction.Completed.Value - _runningAction.Started.Value) }");
                if (!_runningAction.UserCancelled() && _runningAction.Bytes.HasValue)
                    Console.Write($" ({MpbkpuLib.Utility.String.BytesToString(_runningAction.Bytes.Value)})");
                Console.WriteLine();
            }
        } finally {
            Utility.Console.SetCursorVisible(true);
            Console.ResetColor();
        }

        if (succeeded)
            return Success;
        return _runningAction.UserCancelled() ? Cancelled : Failed;
    }
}

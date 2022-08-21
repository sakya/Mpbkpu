using MpbkpuLib;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;

namespace Mpbkpu.Actions;

public abstract class BaseAction : IAction
{
    public const ConsoleColor DefaultColor = ConsoleColor.Gray;
    protected const ConsoleColor HighlightColor = ConsoleColor.White;

    protected const ConsoleColor InfoColor = ConsoleColor.Blue;
    protected const ConsoleColor SuccessColor = ConsoleColor.Green;
    protected const ConsoleColor WarningColor = ConsoleColor.Yellow;
    public const ConsoleColor ErrorColor = ConsoleColor.Red;

    public DateTime? Started { get; set; }
    public DateTime? Completed { get; set; }
    public long? Bytes { get; set; }

    public abstract Task<bool> Run(Arguments pArgs);

    protected void WriteLog(BaseJobSettings.LogLevels level, string message)
    {
        switch (level) {
            case BaseJobSettings.LogLevels.Debug:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                message = $"[DBG]{message}";
                break;
            case BaseJobSettings.LogLevels.Info:
                Console.ForegroundColor = ConsoleColor.White;
                message = $"[INF]{message}";
                break;
            case BaseJobSettings.LogLevels.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                message = $"[WRN]{message}";
                break;
            case BaseJobSettings.LogLevels.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                message = $"[ERR]{message}";
                break;
        }
        Console.WriteLine(message);
        Console.ForegroundColor = DefaultColor;
    }

    protected void WriteEnabled(bool enabled)
    {
        if (enabled)
            Console.ForegroundColor = SuccessColor;
        Console.WriteLine(enabled ? "Enabled" : "Disabled");
        Console.ForegroundColor = DefaultColor;
    }

    protected void OnLog(object sender, BaseJobSettings.LogLevels level, string message)
    {
        Utility.Console.ClearLine();
        WriteLog(level, message);
    }
}
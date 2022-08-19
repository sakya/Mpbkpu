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
    protected const ConsoleColor ErrorColor = ConsoleColor.Red;

    public DateTime? Started { get; set; }
    public DateTime? Completed { get; set; }

    public abstract Task<bool> Run(Arguments pArgs);

    protected async Task<BackupFile.OpenResult?> OpenBackup(string? backupPath, string? password)
    {
        if (string.IsNullOrEmpty(backupPath))
            throw new ArgumentException($"{nameof(backupPath)} cannot be empty", nameof(backupPath));

        Console.ForegroundColor = InfoColor;
        Console.WriteLine("Opening backup");
        Console.ForegroundColor = DefaultColor;
        BackupFile.OpenResult? backupFile;

        try {
            using var fm = FileManagerFactory.GetFileManager(backupPath, out var newPath);
            backupFile = await BackupFile.OpenBackupFile(fm, newPath, password);
        } catch (Exception ex) {
            Console.ForegroundColor = ErrorColor;
            Console.WriteLine($"ERROR: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.StackTrace)) {
                Console.WriteLine(ex.StackTrace);
            }
            return null;
        }

        return backupFile;
    }

    protected void WriteLog(BaseJobSettings.LogLevels level, string message)
    {
        Console.ForegroundColor = level switch
        {
            BaseJobSettings.LogLevels.Debug => ConsoleColor.DarkGray,
            BaseJobSettings.LogLevels.Info => ConsoleColor.White,
            BaseJobSettings.LogLevels.Warning => ConsoleColor.Yellow,
            BaseJobSettings.LogLevels.Error => ConsoleColor.Red,
            _ => Console.ForegroundColor
        };
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
}
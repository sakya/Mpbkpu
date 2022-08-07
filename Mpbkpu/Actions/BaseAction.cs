using Microsoft.Graph;
using MpbkpuLib;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Utility;
using SMBLibrary.SMB1;

namespace Mpbkpu.Actions;

public abstract class BaseAction : IAction
{
    public const ConsoleColor DefaultColor = ConsoleColor.Gray;
    protected const ConsoleColor HighlightColor = ConsoleColor.White;

    protected const ConsoleColor InfoColor = ConsoleColor.Blue;
    protected const ConsoleColor SuccessColor = ConsoleColor.Green;
    protected const ConsoleColor WarningColor = ConsoleColor.Yellow;
    protected const ConsoleColor ErrorColor = ConsoleColor.Red;

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
            backupFile = await BackupFile.OpenBackupFile(backupPath, password);
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

    protected void WriteLog(BaseSettings.LogLevels level, string message)
    {
        Console.ForegroundColor = level switch
        {
            BaseSettings.LogLevels.Debug => ConsoleColor.DarkGray,
            BaseSettings.LogLevels.Info => ConsoleColor.White,
            BaseSettings.LogLevels.Warning => ConsoleColor.Yellow,
            BaseSettings.LogLevels.Error => ConsoleColor.Red,
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;
using MpbkpuLib.Abstracts;
using MpbkpuLib.Jobs;
using Newtonsoft.Json;
using Backup = MpbkpuLib.DataModel.Backup;
using BackupJob = MpbkpuGUI.Models.BackupJob;

namespace MpbkpuGUI.Utility;

/// <summary>
/// A job started by the scheduler of the operating system: this program runs it without a window
/// (<see cref="Argument"/> job id) with the settings and secrets of the user, and writes the result to its own file.
/// The GUI, which may be open at the same time and owns settings.json, merges the results into the jobs
/// (<see cref="MergeResults"/>).
/// </summary>
public static class ScheduledRun
{
    public const string Argument = "--run-job";

    /// <param name="EndedAt">When the run ended (local time)</param>
    /// <param name="PasswordNotSaved">The backup is encrypted and its password is not saved</param>
    public record Result(string JobId, DateTime StartedAt, DateTime EndedAt, Backup.Results Outcome, string? ErrorMessage,
        bool PasswordNotSaved = false);

    private static string Folder(string name)
    {
        var res = Path.Combine(App.LocalPath!, name);
        Directory.CreateDirectory(res);
        return res;
    }

    /// <summary>
    /// The log of the last automatic run of a job
    /// </summary>
    public static string GetLogFile(string jobId) => Path.Combine(Folder("logs"), $"{jobId}.log");

    private static string GetResultFile(string jobId) => Path.Combine(Folder("scheduled"), $"{jobId}.json");

    /// <summary>
    /// Lock a job against a second run, by hand or scheduled, in any process; released when disposed or when the
    /// process ends
    /// </summary>
    /// <returns>Null when the job is already running</returns>
    public static IDisposable? TryLock(string jobId)
    {
        try {
            return new FileStream(Path.Combine(Folder("locks"), $"{jobId}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.DeleteOnClose);
        } catch (IOException) {
            return null;
        }
    }

    /// <summary>
    /// Run a job without a window
    /// </summary>
    /// <returns>Exit code: 0 completed, 1 failed, 2 already running or unknown job</returns>
    public static async Task<int> Run(AppSettings settings, string jobId)
    {
        var job = settings.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job == null)
            return 2;
        using var jobLock = TryLock(jobId);
        if (jobLock == null)
            return 2;

        var started = DateTime.Now;
        var outcome = Backup.Results.Error;
        string? error = null;
        var passwordNotSaved = false;
        try {
            if (job is BackupJob { Encrypted: true } backup && string.IsNullOrEmpty(backup.Password)) {
                passwordNotSaved = true;
            } else {
                var log = GetLogFile(jobId);
                File.Delete(log);
                using var libraryJob = CreateLibraryJob(job, log);
                // No confirmation handler: the job runs as the CLI with -y (a space warning goes to the log)
                var res = await libraryJob.Run(CancellationToken.None);
                outcome = res.Result ?? (res.Success ? Backup.Results.Completed : Backup.Results.Error);
                error = res.ErrorMessage;
            }
        } catch (Exception ex) {
            error = ex.Message;
        }

        WriteResult(new Result(jobId, started, DateTime.Now, outcome, error, passwordNotSaved));
        return outcome is Backup.Results.Completed or Backup.Results.CompletedWithWarnings ? 0 : 1;
    }

    private static BaseJob CreateLibraryJob(Job job, string logFile)
    {
        // The library job opens its log when it is created
        T WithLog<T>(T settings) where T : BaseJobSettings
        {
            settings.LogFile = logFile;
            settings.LogLevel = BaseJobSettings.LogLevels.Info;
            return settings;
        }

        return job switch
        {
            BackupJob backup => new MpbkpuLib.Jobs.BackupJob(WithLog(backup.CreateSettings())),
            SyncJob sync => new SyncDirectoryJob(WithLog(sync.CreateSettings())),
            _ => throw new NotSupportedException(job.GetType().Name)
        };
    }

    private static void WriteResult(Result result)
    {
        // Written aside and renamed, so the GUI never reads half a file
        var file = GetResultFile(result.JobId);
        File.WriteAllText(file + ".tmp", JsonConvert.SerializeObject(result));
        File.Move(file + ".tmp", file, true);
    }

    /// <summary>
    /// Copy the results of the automatic runs that ended after the last known run into the jobs
    /// </summary>
    /// <returns>The results copied (the caller saves the settings and reports failures)</returns>
    public static List<(Job Job, Result Result)> MergeResults(AppSettings settings)
    {
        var res = new List<(Job, Result)>();
        foreach (var job in settings.Jobs) {
            var result = ReadResult(job.Id);
            if (result == null || (job.LastRun != null && result.EndedAt <= job.LastRun))
                continue;

            job.LastRun = result.EndedAt;
            job.LastResult = result.Outcome;
            job.LastRunAutomatic = true;
            res.Add((job, result));
        }
        return res;
    }

    /// <summary>
    /// The result of the last automatic run of a job, null when there is none (or it is being written)
    /// </summary>
    public static Result? ReadResult(string jobId)
    {
        try {
            var file = GetResultFile(jobId);
            return File.Exists(file) ? JsonConvert.DeserializeObject<Result>(File.ReadAllText(file)) : null;
        } catch (Exception) {
            return null;
        }
    }

    /// <summary>
    /// <see cref="MergeResults"/> on the settings of the GUI, saved when something changed
    /// </summary>
    public static async Task<List<(Job Job, Result Result)>> MergeAndSave()
    {
        var merged = MergeResults(App.Settings!);
        if (merged.Count > 0)
            await App.Settings!.Save(App.SettingFile!);
        return merged;
    }

    /// <summary>
    /// Open the log of the last automatic run of a job with the default application
    /// </summary>
    public static async Task OpenLog(Avalonia.Visual visual, string jobId)
    {
        if (Avalonia.Controls.TopLevel.GetTopLevel(visual)?.Launcher is { } launcher)
            await Avalonia.Platform.Storage.LauncherExtensions.LaunchFileInfoAsync(launcher, new FileInfo(GetLogFile(jobId)));
    }

    /// <summary>
    /// Call an action (on any thread) when a result is written
    /// </summary>
    /// <returns>The watcher, to dispose; null when the folder cannot be watched</returns>
    public static IDisposable? WatchResults(Action changed)
    {
        try {
            var watcher = new FileSystemWatcher(Folder("scheduled"), "*.json");
            // Results are renamed into place
            watcher.Renamed += (_, _) => changed();
            watcher.Created += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
            return watcher;
        } catch (Exception) {
            // Merged anyway when the job list is shown
            return null;
        }
    }

    /// <summary>
    /// Remove the files of a deleted job
    /// </summary>
    public static void DeleteFiles(string jobId)
    {
        foreach (var file in new[] { GetResultFile(jobId), GetLogFile(jobId) }) {
            try {
                File.Delete(file);
            } catch (IOException) {
            }
        }
    }
}

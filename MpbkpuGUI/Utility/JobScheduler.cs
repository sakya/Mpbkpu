using System;
using System.Diagnostics;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;

namespace MpbkpuGUI.Utility;

/// <summary>
/// Registers the scheduled jobs in the scheduler of the operating system, for the current user (no administrator rights,
/// no stored password): Windows Task Scheduler, systemd user timers on Linux, launchd agents on macOS. Each entry
/// starts this program with <see cref="ScheduledRun.Argument"/> and the job id.
/// </summary>
public abstract class JobScheduler
{
    /// <summary>
    /// The scheduler of this operating system, null when none is supported
    /// </summary>
    public static JobScheduler? Current { get; } =
        OperatingSystem.IsWindows() ? new WindowsTask() :
        OperatingSystem.IsLinux() ? new SystemdTimer() :
        OperatingSystem.IsMacOS() ? new LaunchAgent() : null;

    /// <summary>
    /// Create, replace or delete the entry of a job to match its schedule
    /// </summary>
    public void Apply(Job job)
    {
        if (job.Schedule is { Enabled: true } schedule && schedule.Days.Count > 0)
            Register(job, schedule);
        else
            Delete(job.Id);
    }

    /// <summary>
    /// Delete the entry of a job, when it exists
    /// </summary>
    public abstract void Delete(string jobId);

    protected abstract void Register(Job job, JobSchedule schedule);

    /// <summary>
    /// The command line started by the scheduler
    /// </summary>
    protected static string Executable => Environment.ProcessPath!;

    protected static string[] Arguments(Job job) => [ScheduledRun.Argument, job.Id];

    /// <summary>
    /// Run a command of the operating system and wait for it
    /// </summary>
    /// <param name="throwOnError">Throw with its error output when it fails, else return false</param>
    protected static bool RunCommand(bool throwOnError, string fileName, params string[] args)
    {
        var info = new ProcessStartInfo(fileName)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        // Both streams read at once: a full pipe would block the command
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEnd();
        output.Wait();
        process.WaitForExit();
        if (process.ExitCode == 0)
            return true;
        if (throwOnError)
            throw new Exception(string.IsNullOrWhiteSpace(error) ? $"{fileName}: exit code {process.ExitCode}" : error.Trim());
        return false;
    }
}

using System;
using System.IO;
using System.Linq;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;

namespace MpbkpuGUI.Utility;

/// <summary>
/// A systemd user timer and service ("mpbkpu-{job id}" in ~/.config/systemd/user). It runs in the session of the user,
/// so the keyring with the saved passwords is available (it would not be with cron), while the user is signed in
/// (or always, with "loginctl enable-linger"); Persistent=true starts a missed run when the computer is on again.
/// </summary>
public class SystemdTimer : JobScheduler
{
    private static string UnitFolder
    {
        get
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(config))
                config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(config, "systemd", "user");
        }
    }

    private static string UnitName(string jobId) => $"mpbkpu-{jobId}";

    protected override void Register(Job job, JobSchedule schedule)
    {
        Directory.CreateDirectory(UnitFolder);
        var name = UnitName(job.Id);
        var description = Escape($"Mpbkpu: {job.Name}");
        var command = string.Join(' ', new[] { Executable }.Concat(Arguments(job)).Select(Quote));
        File.WriteAllText(Path.Combine(UnitFolder, $"{name}.service"),
            $"[Unit]\nDescription={description}\n\n[Service]\nType=oneshot\nExecStart={command}\n");
        var days = string.Join(',', JobSchedule.WeekDays.Where(schedule.Days.Contains).Select(d => d.ToString()[..3]));
        File.WriteAllText(Path.Combine(UnitFolder, $"{name}.timer"),
            $"[Unit]\nDescription={description}\n\n[Timer]\nOnCalendar={days} *-*-* {schedule.Time:hh\\:mm}:00\nPersistent=true\n\n" +
            "[Install]\nWantedBy=timers.target\n");

        RunCommand(true, "systemctl", "--user", "daemon-reload");
        // restart: a changed time is used at once
        RunCommand(true, "systemctl", "--user", "enable", $"{name}.timer");
        RunCommand(true, "systemctl", "--user", "restart", $"{name}.timer");
    }

    public override void Delete(string jobId)
    {
        var name = UnitName(jobId);
        var timer = Path.Combine(UnitFolder, $"{name}.timer");
        if (!File.Exists(timer))
            return;
        RunCommand(false, "systemctl", "--user", "disable", "--now", $"{name}.timer");
        File.Delete(timer);
        File.Delete(Path.Combine(UnitFolder, $"{name}.service"));
        RunCommand(false, "systemctl", "--user", "daemon-reload");
    }

    /// <summary>
    /// A word of ExecStart: double quotes, with the specifiers (%) and quotes escaped
    /// </summary>
    private static string Quote(string value) =>
        $"\"{Escape(value).Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    /// <summary>
    /// '%' starts a specifier in unit files
    /// </summary>
    private static string Escape(string value) => value.Replace("%", "%%").Replace("\n", " ");
}

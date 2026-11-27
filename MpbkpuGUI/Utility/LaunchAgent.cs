using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;

namespace MpbkpuGUI.Utility;

/// <summary>
/// A launchd agent ("com.mpbkpu.job.{job id}" in ~/Library/LaunchAgents). It runs in the session of the user, so the
/// Keychain with the saved passwords is available, while the user is signed in; a run missed while the computer slept
/// starts when it wakes up.
/// </summary>
public class LaunchAgent : JobScheduler
{
    private static string AgentFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");

    private static string Label(string jobId) => $"com.mpbkpu.job.{jobId}";

    private static string PlistFile(string jobId) => Path.Combine(AgentFolder, $"{Label(jobId)}.plist");

    /// <summary>
    /// The launchd domain of the user's session
    /// </summary>
    private static string Domain => $"gui/{GetUid()}";

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint GetUid();

    protected override void Register(Job job, JobSchedule schedule)
    {
        Directory.CreateDirectory(AgentFolder);
        var file = PlistFile(job.Id);
        // Unload the old definition first: launchd keeps the loaded one
        RunCommand(false, "launchctl", "bootout", $"{Domain}/{Label(job.Id)}");
        CreateDefinition(job, schedule).Save(file);
        RunCommand(true, "launchctl", "bootstrap", Domain, file);
    }

    public override void Delete(string jobId)
    {
        var file = PlistFile(jobId);
        if (!File.Exists(file))
            return;
        RunCommand(false, "launchctl", "bootout", $"{Domain}/{Label(jobId)}");
        File.Delete(file);
    }

    private static XDocument CreateDefinition(Job job, JobSchedule schedule)
    {
        // launchd: 0 is Sunday, like DayOfWeek
        var intervals = JobSchedule.WeekDays.Where(schedule.Days.Contains).Select(d => new XElement("dict",
            new XElement("key", "Weekday"), new XElement("integer", (int)d),
            new XElement("key", "Hour"), new XElement("integer", schedule.Time.Hours),
            new XElement("key", "Minute"), new XElement("integer", schedule.Time.Minutes)));
        return new XDocument(
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"),
                new XElement("dict",
                    new XElement("key", "Label"), new XElement("string", Label(job.Id)),
                    new XElement("key", "ProgramArguments"),
                    new XElement("array", new[] { Executable }.Concat(Arguments(job)).Select(a => new XElement("string", a))),
                    new XElement("key", "StartCalendarInterval"), new XElement("array", intervals))));
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;

namespace MpbkpuGUI.Utility;

/// <summary>
/// A Windows Task Scheduler task ("Mpbkpu {job id}"): runs only while the user is signed in (no password stored) and
/// as soon as possible after a missed start (computer off)
/// </summary>
public class WindowsTask : JobScheduler
{
    private static string TaskName(string jobId) => $"Mpbkpu {jobId}";

    protected override void Register(Job job, JobSchedule schedule)
    {
        var file = Path.Combine(Path.GetTempPath(), $"mpbkpu-{job.Id}.xml");
        try {
            // UTF-16, as the declaration of the XML says
            File.WriteAllText(file, CreateDefinition(job, schedule), Encoding.Unicode);
            RunCommand(true, "schtasks.exe", "/Create", "/TN", TaskName(job.Id), "/XML", file, "/F");
        } finally {
            File.Delete(file);
        }
    }

    public override void Delete(string jobId)
    {
        if (RunCommand(false, "schtasks.exe", "/Query", "/TN", TaskName(jobId)))
            RunCommand(true, "schtasks.exe", "/Delete", "/TN", TaskName(jobId), "/F");
    }

    private static string CreateDefinition(Job job, JobSchedule schedule)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        // Any past date: only the time of the day matters
        var start = new DateTime(2026, 1, 1).Add(schedule.Time).ToString("yyyy-MM-ddTHH:mm:ss");
        var task = new XElement(ns + "Task", new XAttribute("version", "1.2"),
            new XElement(ns + "RegistrationInfo",
                new XElement(ns + "Description", $"Mpbkpu: {job.Name}")),
            new XElement(ns + "Triggers",
                new XElement(ns + "CalendarTrigger",
                    new XElement(ns + "StartBoundary", start),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "ScheduleByWeek",
                        new XElement(ns + "DaysOfWeek",
                            JobSchedule.WeekDays.Where(schedule.Days.Contains).Select(d => new XElement(ns + d.ToString()))),
                        new XElement(ns + "WeeksInterval", "1")))),
            new XElement(ns + "Principals",
                new XElement(ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(ns + "UserId", $"{Environment.UserDomainName}\\{Environment.UserName}"),
                    new XElement(ns + "LogonType", "InteractiveToken"),
                    new XElement(ns + "RunLevel", "LeastPrivilege"))),
            new XElement(ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(ns + "StopIfGoingOnBatteries", "false"),
                new XElement(ns + "StartWhenAvailable", "true"),
                new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                new XElement(ns + "Enabled", "true")),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(ns + "Exec",
                    new XElement(ns + "Command", Executable),
                    new XElement(ns + "Arguments", string.Join(' ', Arguments(job))))));
        return new XDeclaration("1.0", "UTF-16", null) + Environment.NewLine + task;
    }
}

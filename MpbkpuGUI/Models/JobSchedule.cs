using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace MpbkpuGUI.Models;

/// <summary>
/// When a job runs by itself (Windows Task Scheduler, see Utility/TaskScheduler)
/// </summary>
public class JobSchedule
{
    /// <summary>
    /// Days of the week, Monday first
    /// </summary>
    public static readonly DayOfWeek[] WeekDays =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
        DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    public bool Enabled { get; set; }
    /// <summary>
    /// Time of the day (local time)
    /// </summary>
    public TimeSpan Time { get; set; } = new(21, 0, 0);
    // Replace: Newtonsoft would add the saved days to the default ones
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<DayOfWeek> Days { get; set; } = WeekDays.ToList();

    /// <summary>
    /// The next run after a moment, null when there is none
    /// </summary>
    public DateTime? NextRun(DateTime after)
    {
        if (!Enabled || Days.Count == 0)
            return null;
        for (var i = 0; i <= 7; i++) {
            var run = after.Date.AddDays(i) + Time;
            if (run > after && Days.Contains(run.DayOfWeek))
                return run;
        }
        return null;
    }

    /// <summary>
    /// "Every day 21:00", "Mon, Wed, Fri 21:00"
    /// </summary>
    public string Describe(CultureInfo culture)
    {
        var time = DateTime.Today.Add(Time).ToString("t", culture);
        if (Days.Count == 7)
            return $"{Localizer.Localizer.Instance["EveryDay"]} {time}";
        var days = WeekDays.Where(Days.Contains).Select(d => culture.DateTimeFormat.GetAbbreviatedDayName(d));
        return $"{string.Join(", ", days)} {time}";
    }
}

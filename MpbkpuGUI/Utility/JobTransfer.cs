using System;
using System.Collections.Generic;
using System.Linq;
using MpbkpuGUI.Abstracts;
using MpbkpuGUI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MpbkpuGUI.Utility;

/// <summary>
/// Export and import of jobs, to move them to another computer. Secrets are never exported: they are
/// <see cref="JsonIgnoreAttribute"/> and live in the OS secure storage.
/// </summary>
public static class JobTransfer
{
    private const int FormatVersion = 1;

    private class ExportFile
    {
        public int Version { get; set; } = FormatVersion;
        public DateTime ExportedAt { get; set; }
        public List<Job> Jobs { get; set; } = new();
    }

    /// <summary>
    /// The file may come from anybody: only types of this program and of the library can be created
    /// </summary>
    private class OwnTypesBinder : DefaultSerializationBinder
    {
        private static readonly HashSet<string?> Allowed =
        [
            typeof(Job).Assembly.GetName().Name,
            typeof(MpbkpuLib.Abstracts.BaseJob).Assembly.GetName().Name
        ];

        public override Type BindToType(string? assemblyName, string typeName)
        {
            if (!Allowed.Contains(assemblyName))
                throw new JsonSerializationException($"Type not allowed: {typeName}, {assemblyName}");
            return base.BindToType(assemblyName, typeName);
        }
    }

    private static JsonSerializerSettings SerializerSettings => new()
    {
        TypeNameHandling = TypeNameHandling.Objects,
        SerializationBinder = new OwnTypesBinder(),
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented
    };

    /// <summary>
    /// The jobs as the content of an export file, without secrets
    /// </summary>
    public static string Export(IEnumerable<Job> jobs)
    {
        return JsonConvert.SerializeObject(new ExportFile() { ExportedAt = DateTime.Now, Jobs = jobs.ToList() }, SerializerSettings);
    }

    /// <summary>
    /// The jobs of an export file, ready to be added: new ids, no run history, schedules off (the passwords must
    /// be entered again first)
    /// </summary>
    /// <exception cref="FormatException">Not an export file, or one written by a newer version</exception>
    public static List<Job> Import(string content)
    {
        ExportFile? file;
        try {
            file = JsonConvert.DeserializeObject<ExportFile>(content, SerializerSettings);
        } catch (JsonException ex) {
            throw new FormatException(ex.Message, ex);
        }
        if (file == null || file.Jobs.Count == 0 || file.Jobs.Any(j => j == null))
            throw new FormatException("No jobs in the file");
        if (file.Version > FormatVersion)
            throw new FormatException($"File version {file.Version} not supported");

        foreach (var job in file.Jobs) {
            // The id names the secrets, logs and scheduler entries of the job on this computer.
            // The ids of the positions are kept: the backup databases record the destination of each volume with them.
            job.Id = Guid.NewGuid().ToString("N");
            job.LastRun = null;
            job.LastResult = null;
            job.LastRunAutomatic = false;
            if (job.Schedule != null)
                job.Schedule.Enabled = false;
            if (job is BackupJob backup)
                backup.RememberPassword = false;
        }
        return file.Jobs;
    }
}

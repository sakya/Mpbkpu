using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.FileManagers;
using MpbkpuLib.Interfaces;
using MpbkpuLib.Utility;

namespace MpbkpuLib.Jobs;

/// <summary>
/// The destinations of a backup opened by a job: one file manager per destination, the database read from the newest
/// copy and the mapping between the destinations of the settings and the ones recorded in the database.
/// </summary>
public sealed class DestinationSet : IDisposable
{
    /// <summary>
    /// An opened destination
    /// </summary>
    public sealed class Entry
    {
        internal Entry(BackupDestination settings, IFileManager fm, string path)
        {
            Settings = settings;
            Fm = fm;
            Path = path;
        }

        public BackupDestination Settings { get; }
        public IFileManager Fm { get; }
        /// <summary>
        /// The destination directory in the form used by <see cref="Fm"/>
        /// </summary>
        public string Path { get; }
        /// <summary>
        /// The id of the <see cref="DataModel.Destination"/> record, set by <see cref="Register"/>
        /// </summary>
        public int? RecordId { get; internal set; }
        public SpaceEstimate Space { get; } = new();
    }

    public DestinationSet(IEnumerable<BackupDestination> destinations)
    {
        try {
            foreach (var destination in destinations) {
                var fm = FileManagerFactory.GetFileManager(destination.Path, out var path, destination.User, destination.Password, destination.OauthSettings);
                Entries.Add(new Entry(destination, fm, path));
            }
        } catch {
            Dispose();
            throw;
        }
    }

    public List<Entry> Entries { get; } = new();

    /// <summary>
    /// The destination the database was read from, null when the database is new
    /// </summary>
    public Entry? DatabaseSource { get; private set; }

    /// <summary>
    /// Open the newest copy of the database (by modification date). With more than one destination the database is
    /// always copied to a temporary file, to be written to every destination with <see cref="BackupFile.SaveBackupFile"/>.
    /// </summary>
    public async Task<BackupFile.OpenResult> OpenDatabase(string? password, bool create, Action<long, long>? progressAction, CancellationToken? token)
    {
        DatabaseSource = null;
        DateTime? newest = null;
        foreach (var entry in Entries) {
            var modified = await GetDatabaseDate(entry);
            if (modified != null && (newest == null || modified > newest)) {
                newest = modified;
                DatabaseSource = entry;
            }
        }

        var source = DatabaseSource ?? Entries[0];
        return await BackupFile.OpenBackupFile(source.Fm, source.Path, password, create, progressAction, token, Entries.Count > 1);
    }

    /// <summary>
    /// Match the destinations with the ones recorded in the database (by id, then by path, so that a destination
    /// keeps its record when its id is not persisted), record the new ones and assign the volumes created before
    /// multiple destinations to the destination the database was read from.
    /// </summary>
    public async Task Register(BackupFile backupFile)
    {
        var records = await backupFile.Destinations.ToListAsync();
        var matched = new Dictionary<Entry, DataModel.Destination>();
        foreach (var entry in Entries) {
            var record = records.FirstOrDefault(r => r.Key == entry.Settings.Id);
            if (record != null) {
                matched[entry] = record;
                records.Remove(record);
            }
        }

        foreach (var entry in Entries) {
            var path = MaskedPath(entry);
            if (!matched.TryGetValue(entry, out var record)) {
                record = records.FirstOrDefault(r => r.Path == path);
                if (record != null)
                    records.Remove(record);
                else
                    backupFile.Destinations.Add(record = new DataModel.Destination());
                matched[entry] = record;
            }
            record.Key = entry.Settings.Id;
            record.Path = path;
        }
        await backupFile.SaveChangesAsync();

        foreach (var (entry, record) in matched)
            entry.RecordId = record.Id;

        if (DatabaseSource != null) {
            await backupFile.BackupVolumes
                .Where(v => v.DestinationId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.DestinationId, DatabaseSource.RecordId));
        }
    }

    public void Dispose()
    {
        foreach (var entry in Entries)
            entry.Fm.Dispose();
        Entries.Clear();
    }

    /// <summary>
    /// The path of a destination as recorded in the database (credentials masked)
    /// </summary>
    public static string MaskedPath(Entry entry)
    {
        return BaseJob.MaskCredentials(entry.Settings.Path ?? string.Empty);
    }

    /// <summary>
    /// The modification date of the database in a destination (or of its backup copy), null when there is none
    /// </summary>
    private static async Task<DateTime?> GetDatabaseDate(Entry entry)
    {
        if (!await entry.Fm.DirectoryExists(entry.Path))
            return null;
        foreach (var name in new[] { BackupFile.DefaultDatabaseFileName, BackupFile.DatabaseBackupFileName }) {
            var path = entry.Fm.CombinePath(entry.Path, name);
            if (await entry.Fm.FileExists(path))
                return (await entry.Fm.GetFileInfo(path)).LastModified ?? DateTime.MinValue;
        }
        return null;
    }
}

using Microsoft.EntityFrameworkCore;
using MpbkpuLib.Abstracts;
using MpbkpuLib.DataModel;
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
        /// The id of the <see cref="DataModel.Destination"/> record, set by <see cref="Match"/> and <see cref="Register"/>
        /// </summary>
        public int? RecordId { get; internal set; }
        public SpaceEstimate Space { get; } = new();
    }

    /// <summary>
    /// Paths of the destinations recorded in the database, by record id
    /// </summary>
    private Dictionary<int, string?> _recordPaths = new();

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
    /// Check a list of destinations: at least one, every one with a path and an id, no duplicate ids, no destination
    /// inside another one
    /// </summary>
    public static void Validate(IReadOnlyList<BackupDestination> destinations, string name)
    {
        if (destinations.Count == 0)
            throw new ArgumentNullException(name, "At least one destination is required");

        for (var i = 0; i < destinations.Count; i++) {
            var destination = destinations[i];
            if (string.IsNullOrEmpty(destination.Path))
                throw new ArgumentNullException(name, "Destination cannot be empty");
            if (string.IsNullOrEmpty(destination.Id))
                throw new ArgumentNullException(name, "Destination id cannot be empty");

            for (var j = i + 1; j < destinations.Count; j++) {
                var other = destinations[j];
                if (other.Id == destination.Id)
                    throw new ArgumentException($"Duplicate destination id {destination.Id}");
                if (!string.IsNullOrEmpty(other.Path) &&
                    (Files.IsSameOrSubPath(destination.Path, other.Path) || Files.IsSameOrSubPath(other.Path, destination.Path)))
                    throw new ArgumentException($"Destinations {destination.Path} and {other.Path} overlap");
            }
        }
    }

    /// <summary>
    /// Open the newest copy of the database (by modification date) and match the destinations with the ones recorded
    /// in it (<see cref="Match"/>). With more than one destination the database is always copied to a temporary file,
    /// to be written to every destination with <see cref="BackupFile.SaveBackupFile"/>.
    /// </summary>
    public async Task<BackupFile.OpenResult> OpenDatabase(string? password, bool create, Action<long, long>? progressAction, CancellationToken? token)
    {
        DatabaseSource = null;
        DateTime? newest = null;
        var withDatabase = new List<Entry>();
        foreach (var entry in Entries) {
            var modified = await GetDatabaseDate(entry);
            if (modified != null)
                withDatabase.Add(entry);
            if (modified != null && (newest == null || modified > newest)) {
                newest = modified;
                DatabaseSource = entry;
            }
        }

        var source = DatabaseSource ?? Entries[0];
        var res = await BackupFile.OpenBackupFile(source.Fm, source.Path, password, create, progressAction, token, Entries.Count > 1);
        try {
            await Match(res.BackupFile);
            // Every copy records the destinations of its backup (Register): a destination with a database the newest
            // copy does not record holds another backup. Databases without records (before multiple destinations)
            // cannot be checked.
            var foreign = withDatabase.FirstOrDefault(e => e.RecordId == null);
            if (foreign != null && _recordPaths.Count > 0)
                throw new ForeignDatabaseException(MaskedPath(DatabaseSource!), MaskedPath(foreign));
        } catch {
            res.Dispose();
            throw;
        }
        return res;
    }

    /// <summary>
    /// Match the destinations with the ones recorded in the database, without changing it: by id, then by path, so
    /// that a destination keeps its record when its id is not persisted
    /// </summary>
    public async Task Match(BackupFile backupFile)
    {
        var records = await backupFile.Destinations.AsNoTracking().ToListAsync();
        _recordPaths = records.ToDictionary(r => r.Id, r => r.Path);
        foreach (var entry in Entries)
            entry.RecordId = null;
        foreach (var (entry, record) in MatchRecords(records))
            entry.RecordId = record.Id;
    }

    /// <summary>
    /// Record the destinations in the database (new ones added, ids and paths updated) and assign the volumes created
    /// before multiple destinations to the destination the database was read from
    /// </summary>
    public async Task Register(BackupFile backupFile)
    {
        var records = await backupFile.Destinations.ToListAsync();
        var matched = MatchRecords(records);
        foreach (var entry in Entries) {
            if (!matched.TryGetValue(entry, out var record)) {
                backupFile.Destinations.Add(record = new Destination());
                matched[entry] = record;
            }
            record.Key = entry.Settings.Id;
            record.Path = MaskedPath(entry);
        }
        await backupFile.SaveChangesAsync();

        foreach (var (entry, record) in matched) {
            entry.RecordId = record.Id;
            _recordPaths[record.Id] = record.Path;
        }

        if (DatabaseSource != null) {
            await backupFile.BackupVolumes
                .Where(v => v.DestinationId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.DestinationId, DatabaseSource.RecordId));
        }
    }

    /// <summary>
    /// The destination holding a volume
    /// </summary>
    /// <exception cref="MissingDestinationException">The destination of the volume has not been given to the job</exception>
    public Entry GetEntry(BackupVolume volume)
    {
        // Volumes created before multiple destinations are where the database was
        if (volume.DestinationId == null)
            return DatabaseSource ?? Entries[0];

        return Entries.FirstOrDefault(e => e.RecordId == volume.DestinationId) ??
               throw new MissingDestinationException(_recordPaths.GetValueOrDefault(volume.DestinationId.Value));
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
    /// The records of the destinations: by id first, then by path among the records not matched by id
    /// </summary>
    private Dictionary<Entry, Destination> MatchRecords(List<Destination> records)
    {
        var free = records.ToList();
        var res = new Dictionary<Entry, Destination>();
        foreach (var entry in Entries) {
            var record = free.FirstOrDefault(r => r.Key == entry.Settings.Id);
            if (record != null) {
                res[entry] = record;
                free.Remove(record);
            }
        }

        foreach (var entry in Entries.Where(e => !res.ContainsKey(e))) {
            var path = MaskedPath(entry);
            var record = free.FirstOrDefault(r => r.Path == path);
            if (record != null) {
                res[entry] = record;
                free.Remove(record);
            }
        }
        return res;
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

# Decisions

## Platform and dependencies
- **Avalonia 12 with reflection bindings and the name generator disabled** (`AvaloniaUseCompiledBindingsByDefault=false`,
  `AvaloniaNameGeneratorIsEnabled=false`). The code uses `FindControl` and bindings without `x:DataType`; the
  generated fields hid `MainWindowBase.Container` and `StyledElement.Name`.
- **Material.Icons.Avalonia** instead of Projektanker/FontAwesome: Projektanker does not support Avalonia 12.
- **EF Core 10**, not 11: version 11 requires .NET 11.
- **Inter font** from `Avalonia.Fonts.Inter` (`avares://Avalonia.Fonts.Inter/Assets#Inter`).

## Backup
- **Differential backups compare with the last full backup**; deletion records carry `OriginalRelativePath`.
- **Chains**: a full backup always starts a new chain; after an incomplete full backup the next backup is a full one;
  switching between incremental and differential → new full backup. This keeps the existing reconstruction
  (`GetBackupItems`) valid for old backups too.
- **Retention** (`KeepChains`): runs after a completed full backup, reusing `DeleteJob`; a failure is only a warning.
- **Safe writes**: restore to a temporary file + rename after the hash check; database copied as `mpbkpu.db.tmp` and
  then renamed; `DeleteJob` deletes the storage files only after the database commit and copy.
- **No VSS** (native dependency + admin rights): locked files skipped with a warning, files changed during the copy
  retried; warnings stored in `BackupWarnings`, result `CompletedWithWarnings` (treated as completed).
- **Encryption v2**: AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC), header `MPBKPU_ENCRYPT02`; keys derived once per
  `EncryptDecrypt` instance. Format v1 is still readable.
- **Local paths without a drive letter** (relative, UNC) → file system of the current OS.
- **Passwords are never serialized**: Newtonsoft `[JsonIgnore]` (job settings, `OAuth2Settings.AppSecret`, GUI
  `BackupJob.Password`). Newtonsoft is the serializer used everywhere.
- **Folder synchronization** (`SyncDirectoryJob`): source and destination items are matched by relative path with
  '/' as separator (`ToPortablePath`), so different file managers can be compared. Hidden and excluded items are
  skipped on both sides, so they are never deleted from the destination; the content of unreadable source
  directories is never deleted. Files are written to `<name>.mpbkpu-tmp` and renamed when complete. `Exclude`
  patterns (wildcards, case-insensitive) match the relative path or the file name. The job result
  (`BaseJobResult.Result`) is null when there was nothing to do.
- **Uploads from a stream** (`Utility/UploadStream`): the job writes into a `System.IO.Pipelines` pipe and the
  upload API reads the declared length from the other side. More data than declared → `IOException`; less →
  the upload is cancelled and `CompleteAsync` throws (never a truncated remote file); a failed upload completes
  the pipe so the writer never waits forever.
- **MEGA**: the node list is read once per `Mega` instance and updated by its own operations (other clients'
  changes during a job are not seen). Paths always start from the Cloud Drive root; with duplicate names folders
  win, then the most recent item. Deletes are permanent (not to the trash).
- **Nextcloud**: `NextcloudClient` builds WebDAV URLs with `Client.GetDavUrl` (escaped segments) and converts
  PROPFIND hrefs back with `GetPathFromHref`; one `HttpClient` without timeout for everything. OAuth refresh
  tokens are single use: `Client.TokensRefreshed` saves every new token in the cache.
- **Local file systems**: dates are UTC (`LastWriteTimeUtc`); `Files.SameModificationTime` also accepts the local
  times saved by older versions, so existing backups are not copied again. Only real links (`LinkTarget != null`)
  are skipped: other reparse points (OneDrive/cloud placeholders, dedup) are normal directories. Directories are
  deleted with a custom recursion that removes links without following them and clears read-only attributes.
- **FTP/SFTP**: full paths returned by listings keep the form of the requested path. Names starting with '.' are
  hidden. FTP validates TLS certificates (no more `ValidateAnyCertificate`) and checks the server reply after an
  upload. SFTP trusts the first host key of a server and saves it in `%LOCALAPPDATA%\Mpbkpu\known_hosts`
  (`Sftp.KnownHostsFile`); a different key later is rejected.
- **OneDrive**: `TokenProvider` asks MSAL for a token on every request (MSAL caches and refreshes it). Items are
  found with `Root.ItemWithPath` (the SDK escapes the path; `Items["root:/path:"]` would be escaped by the URL
  template) or `Items["root"]`, then addressed by id. Deleted items go to the OneDrive recycle bin.
- **SMB** (EzSmb): `Node.Move` takes a path relative to the parent folder, `Node.Write` writes `stream.Length`
  bytes reading in order and does not truncate (existing files are deleted first), folders are deleted only when
  empty (recursion done by `Smb.DeleteDirectory`), `GetList` already skips "."/"..". EzSmb resolves host names
  to IP addresses, so full paths are built from the requested path.
- **Time left** (`EtaEstimator`): work = bytes + items x fixed cost (256 KiB local, 2 MiB when a remote file
  manager is involved), speed over the last 30 s, null (`--:--:--`) during the first 3 s or when nothing moves.
  Every phase (copy, deletion, verification) starts its own estimate with `BaseJob.StartEta`.
- **Free space** (`IFileManager.GetSpace` → `StorageSpace(Available, Total)`), null when the file system cannot
  report it. Cloud services return the space of the account (Dropbox team: of the team). Jobs read it at the start and
  at the end and estimate it in between (`BaseJob.SpaceChanged`); the GUI saves the last value in the job, reads local
  destinations live and remote ones only on request (refresh button) or during a run, to avoid logins at startup.
- **Remembered encryption password** (GUI only): opt-in per job (`BackupJob.RememberPassword`, default off), stored
  in `SecretStore` like the other secrets, never in `settings.json`. A wrong saved password is detected by the
  dedicated `MpbkpuLib.InvalidPasswordException` and forgotten. Without a secure storage the option is hidden and the
  settings are saved without secrets.
- **Credentials in paths** (`ftp://user:password@host`, `mega://user:password/path`, ...) are masked by
  `BaseJob.SerializeSettings` before the settings are logged or saved in the backup database.
- **GUI credentials in the OS secure storage**: `Job.Position.Password` and `OauthAppSecret` are `[JsonIgnore]` and
  saved by `AppSettings.Save` in `SecretStore` (MSAL extensions storage, already a dependency: DPAPI / Keychain /
  libsecret), keys `{jobId}.{Source|Destination}.{field}`. Old plain-text values are read through private
  `Legacy*` setters and removed at startup.

## Multiple destinations (in progress, see TODO.md)
- **Split, not mirrored**: every file of a backup is stored in one destination only; every destination holds a copy
  of the database (needed to find the files). A file goes to the destination with the fewest assigned bytes among
  those with enough estimated space.
- **A destination that fails** fails the job after `RetryOnError` tries (no reassignment to another destination).
- **`ParallelFiles`** (default 1) is the total number of files copied at the same time, further limited by what each
  file manager (source included) supports; FTP, SFTP, SMB and MEGA copy one file at a time per destination until
  they are proven safe with concurrent transfers (one connection per instance; more connections would mean more
  logins and concurrent OAuth refreshes).
- **Settings**: `BackupJobSettings.Destinations` (`BackupDestination`: `Id`, `Path`, `User`, `Password`
  `[JsonIgnore]`, `OauthSettings`). The old single-destination keys (`Destination`, `DestinationUser`,
  `DestinationOauthSettings`) are read by private write-only properties into the first destination and never written
  again: the settings saved in old backup databases still deserialize.
- **Database**: table `Destinations` (`Key` = `BackupDestination.Id`, unique; `Path` with masked credentials) and
  nullable `BackupVolume.DestinationId` (migration `MultipleDestinations`). Null = a volume created before multiple
  destinations, stored in the destination the database was read from: the backup job must assign those volumes to
  that destination before it writes the database to other destinations.
- **`DestinationSet`** (shared by the jobs): one file manager per destination; the database is read from the newest
  copy (modification date of `mpbkpu.db`, or of `mpbkpu.db.bak` when missing), always through a temporary file when
  there is more than one destination (`OpenBackupFile(copyToTemp)`); `Register` matches the destinations with the
  `Destinations` records by id, then by masked path, so a destination whose id is not persisted (CLI, GUI today)
  keeps its record as long as its path does not change; it records new destinations and assigns the legacy volumes
  (null `DestinationId`) to the destination the database was read from.
- **Database written to every destination** with `BackupFile.SaveBackupFile` (moved from `BaseJob`): with more than
  one destination a failure is a warning (job result `CompletedWithWarnings`, the database inside still says
  `Completed`) as long as one copy succeeds; when all fail the job fails and the new files are deleted. A stale copy
  is replaced by the next run, which reads the newest one.
- **Risk not handled**: a destination holding the database of another backup set would be read if newer. There is no
  backup set identifier to detect it.
- **Space**: `Utility/SpaceEstimate` (read + estimated use) per destination; `SpaceChanged` reports the sum of the
  known spaces until the GUI shows one bar per destination.
- **The backup directory `{guid}`** is created in a destination only when its first file goes there; volumes have
  their destination, so a volume never mixes destinations.
- **`BackupDestination.Id` should still be stable** (GUI: persisted per destination): matching by path does not survive
  a destination moved to a new path.
- **Parallel copies** (`BackupJob`): one reader pages the files from the database (by id, 500 at a time) into a
  bounded `Channel`; `min(ParallelFiles, source limit, sum of the destination limits)` workers copy them, so a worker
  always finds a destination with a free place (no waiting on destinations). One code path: `ParallelFiles = 1` is
  the old sequential copy. The first failing worker cancels the others (linked token) and its error is rethrown;
  a user cancellation still ends with the `Cancelled` result.
- **`IFileManager.MaxParallelTransfers`**: 1 for every file manager for now (the user's choice, 2026-11-25). Since
  the source counts too, the workers are 1 and nothing is copied in parallel until a limit is raised (local file
  systems and the HTTP services OneDrive, Dropbox, Nextcloud are the candidates). `NextcloudClient` already refreshes
  the token once under a lock (refresh tokens are single use).
- **Shared state**: the `DbContext` is used under one `SemaphoreSlim` (`WithDatabase`); volumes and the backup
  directory are created under a lock per destination and counted in memory (volumes belong to the running backup);
  progress counts copied bytes exactly (failed tries are subtracted) and `Progress`/`FileProgress` are raised one at a
  time; logging is serialized in `BaseJob.Log`. One `EncryptDecrypt` per worker.
- **Files are copied in id order**, no longer in relative path order (nothing depends on it).
- **Jobs on existing backups** (list, list content, restore, verify, delete) take `BackupDestinations`
  (`BackupDestinationsJobSettings`, replacing `BackupPath*`): `BaseJob.OpenBackupDestinations` opens the newest
  database and matches the destinations read-only (`DestinationSet.Match`, nothing written); `GetBackupItemLocation`
  finds each item in the destination of its volume and throws `MissingDestinationException` (with the recorded,
  masked path) when that destination was not given. Verify reports it as `FileNotFound` per item; restore fails.
- **Delete** removes the files from their destinations, the `{guid}` directory from every destination and writes the
  database to all of them (`BaseJob.SaveDatabaseToDestinations`, shared with the backup job). `KeepChains` therefore
  works with multiple destinations.
- **`DestinationSet.Validate`** is shared by all the jobs (at least one destination, paths and ids set, no duplicate
  ids, no overlapping destinations); the backup job adds the source checks.
- **CLI**: `-d/--destination` (backup) and `-b/--backup` (list, list content, restore, verify, delete) can be
  repeated; the user, password and OAuth options apply to the first path, the others take the credentials in the path
  (`scheme://user:password@host`). Restore and sync take one destination (`Arguments.Validate`, reported like the
  parser errors). `--parallelFiles` (default 1) sets `ParallelFiles`. The CLI does not persist destination ids: the
  jobs match the recorded destinations by path. Built by `Arguments.GetDestinations` / `GetBackupDestinations`.
- **Restore does not retry a `MissingDestinationException`**: the destination does not appear by retrying.
- **GUI helper**: `Job.Position.ToBackupDestination()` builds the single destination the GUI passes today.

## GUI
- **SingleWindow navigation unchanged** (pages animated in `Container`, back arrow in the TitleBar); the sidebar is
  only on the job list.
- **Cards with `ItemsControl`** instead of `ItemsList` for the job list and the history: `ItemsList` colors the row
  backgrounds and conflicts with the cards. `ItemsList` stays in the file browser (selection is needed there).
- **Centralized styling** in `App.axaml` through Fluent resource overrides and classes (`card`, `chip`, `h1`, `accent`,
  `danger`, `icon`, `nav`, `log`…).
- **Result of the last run** saved in the job (`LastResult`) on every run, also on error or cancellation.
- **Message dialogs**: buttons on the right, secondary before primary. A Yes/No confirmation with the warning icon is
  destructive and its confirm button uses `danger`: every delete/abort confirmation already used that icon, so no
  extra parameter is needed.
- **File system types** in the job settings are sorted by their name in the current language; a new position is a
  local folder.
- **Windows animations with the custom title bar**: a Win32 window styles callback adds `WS_CAPTION` (DWM needs it for
  the open/minimize/maximize/restore animations) and removes `WS_SYSMENU` (native caption buttons);
  `WindowDecorations.BorderOnly` still hides the native caption. Same fix as Darsena (commit `1fdf4834`).

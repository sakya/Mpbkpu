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

## Multiple destinations
- **Split, not mirrored**: every file of a backup is stored in one destination only; every destination holds a copy
  of the database (needed to find the files). A file goes to the destination with the fewest assigned bytes among
  those with enough estimated space.
- **A destination that fails** fails the job after `RetryOnError` tries (no reassignment to another destination).
- **`ParallelFiles`** (default 1) is the total number of files copied at the same time, further limited by what each
  file manager (source included) supports (`MaxParallelTransfers`, below). Several connections per destination were
  rejected: more logins and concurrent OAuth refreshes.
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
  `Destinations` records by id, then by masked path, so a destination whose id is not persisted (CLI) keeps its
  record as long as its path does not change (a moved destination needs the persisted id of the GUI); it records new
  destinations and assigns the legacy volumes (null `DestinationId`) to the destination the database was read from.
- **Database written to every destination** with `BackupFile.SaveBackupFile` (moved from `BaseJob`): with more than
  one destination a failure is a warning (job result `CompletedWithWarnings`, the database inside still says
  `Completed`) as long as one copy succeeds; when all fail the job fails and the new files are deleted. A stale copy
  is replaced by the next run, which reads the newest one.
- **Risk not handled**: a destination holding the database of another backup set would be read if newer. There is no
  backup set identifier to detect it.
- **The backup directory `{guid}`** is created in a destination only when its first file goes there; volumes have
  their destination, so a volume never mixes destinations.
- **Parallel copies** (`BackupJob`): one reader pages the files from the database (by id, 500 at a time) into a
  bounded `Channel`; `min(ParallelFiles, source limit, sum of the destination limits)` workers copy them, so a worker
  always finds a destination with a free place (no waiting on destinations). One code path: `ParallelFiles = 1` is
  the old sequential copy. The first failing worker cancels the others (linked token) and its error is rethrown;
  a user cancellation still ends with the `Cancelled` result.
- **`IFileManager.MaxParallelTransfers`**: the transfers an instance can run safely at the same time, for reads and
  writes alike (one limit, not two: a worker reads, compresses, encrypts and writes the same file, so parallel reads
  would only pay off with a read-ahead pipeline; the bottleneck of a remote destination is the network). Local file
  systems: no limit (no state per instance; `ParallelFiles` decides, also for slow disks), since 2026-11-26. Every
  remote file manager: 1 (the user's choice, 2026-11-25; one connection per instance), so the workers are 1 when the
  source or every destination is remote. Next candidates: OneDrive, Dropbox, Nextcloud. `NextcloudClient` already
  refreshes the token once under a lock (refresh tokens are single use).
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
- **GUI**: a backup job keeps its first destination in `Job.Destination` (old settings files load unchanged) and the
  others in `BackupJob.MoreDestinations`; `Job.Destinations` lists them all, `Job.GetBackupDestinations()` converts
  them for the library. Every `Position` has a persisted `Id` (positions saved before get one when loaded, kept from
  the next save; until then the jobs match the destinations by path). `PositionDetail` keeps the id and the cached
  space of the edited position. Secrets of the other destinations: `{jobId}.Destination.{positionId}.{field}`.
- **Space per destination**: `Utility/SpaceEstimate` (read + estimated use) per destination; `SpaceChanged` carries
  the `BackupDestination.Id` (null for a synchronization);
  `Position.Space`/`SpaceRead` replace `Job.DestinationSpace` (a cache: the old value is dropped and read again).
  `Controls/DestinationSpaceView` shows one row per destination in the job list (path trimmed from the left with
  `PathSegmentEllipsis` when there is more than one); the running page shows one bar per destination.
- **History, verify, delete pages** use every destination of the job (`DestinationSet.OpenDatabase` for the history,
  the job destinations for verify instead of the paths saved in the backup settings, which are masked and have no
  password).
- **Files being copied**: `FileProgress` adds a file (its first report), `BaseJob.FileCompleted` removes it; it is
  raised for every file that leaves the copy, also failed or cancelled (a failed file never reports 100%). The GUI
  running page lists them under the progress tiles (sync: one at a time). The CLI progress line still shows the last
  file reported, so with parallel copies it alternates between files.

## GUI
- **Themes**: `App.axaml` keeps one color set per theme variant in `ThemeDictionaries` (Dark: the original palette,
  Light); the brushes (`BgBrush`, `TextBrush`, ...) take their color with `DynamicResource`, and every page refers to
  colors and brushes with `DynamicResource`, so the theme changes without restarting. No fixed colors in pages or
  code: add a color to both dictionaries (code binds with `GetResourceObservable`). `AppSettings.Theme`
  (System, the default, Light, Dark) is applied by `App.ApplyTheme` at startup and by the sidebar button that cycles
  it.
- **Copy command** (job list): `MpbkpuLib.Utility.CommandLine` builds the CLI command of a backup or sync job from the
  same settings the GUI runs (`BackupJob.CreateSettings`, `SyncJob.CreateSettings`), in PowerShell syntax (single
  quotes, `&` before a quoted executable: `mpbkpu.exe` next to the GUI, else `mpbkpu` from the PATH), with `-y` and
  only the options that differ from the CLI defaults. No secret is written: an encrypted backup gets `-i`, passwords
  and OAuth secrets become `<password>`. The CLI takes credentials only for the first destination, so the others get
  them in the path (`scheme://user:<password>@host/...` for FTP/SFTP/SMB, `scheme://user:<password>/...` for
  MEGA/MediaFire); destinations with OAuth settings or a password that cannot go in the path (Nextcloud) are listed
  in the dialog as not reachable. `Tests/CommandLine.cs` parses the commands with the CLI `Arguments` (the test
  project references `Mpbkpu`), so a renamed option fails a test.
- **Backup browser**: double click on a directory of the file list selects it in the tree (its ancestors are
  expanded first); right click on a file, a directory or a tree node selects it (`ItemsList` on the right button
  press, the TreeView by itself) and offers "Download...": a folder picker, then
  `RestoreJob` with `RestoreJobSettings.RelativePath`, which restores that item (a directory with its content) into
  the folder with its name and without its parent directories, overwriting existing files and deleting nothing
  (the destination is not read). The job settings come from the job (destinations, password) as for verify.
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

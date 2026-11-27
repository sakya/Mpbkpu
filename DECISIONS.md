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
- **Path options** (`?name=value&...` at the end of a path, `FileManagerFactory.SplitOptions`): settings of a
  position that are not secrets, so GUI, CLI and database take them with the path. FTP: `tls=required` (explicit TLS,
  no plain FTP that would send the password in clear), `fingerprint=` (SHA-256 of a certificate to accept although
  it does not validate, e.g. self-signed; the connection error shows the fingerprint to add). SFTP: `hostkey=`
  (accept a changed host key, which replaces the saved one; the error shows it). Secrets stay in the password:
  SFTP `keyfile=path;passphrase=...`. The GUI shows the options under the path (`PositionDetailModel.PathHint`).
- **Modification date on upload**: `IFileManager.OpenFile(..., lastModified)` for the file systems that can set it
  only when creating a file (MEGA); sync and restore pass it, the others ignore it.
- **pCloud** (`FileManagers/PCloud.cs`, 2026-11-27): own HTTP client (no maintained .NET SDK) on the JSON API, items
  addressed by path (`pcloud://account/position`). OAuth 2.0 code flow with the app of the user (app id, secret,
  redirect url, like Dropbox); the token does not expire and is saved with the API server of the account
  (`api.pcloud.com` US, `eapi.pcloud.com` Europe; any other host from the redirect is rejected, so the token is
  never sent elsewhere); the account of the path must be the one that logs in. Result codes 2002/2005/2009 are "not
  found". Uploads: `uploadfile` multipart through `UploadStream` with `nopartial=1` and `mtime` (not verified that it
  is honoured), an existing file is deleted first; downloads from the `getfilelink` url. Sources: docs.pcloud.com,
  the official JS SDK and rclone; not tried on a real account.
- **OAuth browser login** (`Utility/OAuthBrowserLogin`): opens the authorization page and waits for the redirect on a
  local `HttpListener` (a page turns the fragment into a query string); shared by Dropbox and pCloud.
  `OAuthTokenCache` keeps tokens without refresh token or expiry and an `ApiHost`.
- **Account paths** (`FileManagerFactory.GetAccount`): OneDrive, Dropbox, Nextcloud and pCloud read the account from
  `scheme://account/position` (else from the user) the same way.
- **Compression on remote destinations** (2026-11-27): a compressed file has an unknown size, so a destination
  supports compression (`IFileManager.SupportCompression`) only if it can upload a stream of unknown length.
  Compressing to a temporary file first was rejected by the user (time and disk space with large files), and
  compressing twice to measure the size costs as much. Supported: local, FTP, SFTP (streams), Dropbox (upload
  session, the end found by a chunk shorter than 4 MB), Nextcloud (PUT with chunked transfer encoding, to verify on a
  real server). Not supported: OneDrive (Graph wants the total size in every fragment), MEGA and MediaFire (the size
  is part of the upload protocol), SMB (EzSmb writes streams of known length; it claimed support and failed on the
  first file), pCloud (rclone does not upload unknown sizes and pCloud refuses chunked encoding). `UploadStream`
  takes a null length; such an upload is committed only after `FileSystemStream.EndOfData()`, called by the backup
  after the last byte, so a copy interrupted by an error is cancelled instead of becoming a truncated file. The GUI
  checks the destinations when the job is saved (`Job.Position.SupportsCompression`, read from the file manager).
- **MEGA 2FA**: the code is asked, not stored (the user's choice, 2026-11-27): a login failing with
  `TwoFactorAuthenticationError` calls `Mega.TwoFactorCodeProvider` (GUI: `PasswordDialog` with a custom label, at
  least 6 characters; CLI: a console line). The session is kept in memory per account and reused by the other
  instances of the process, so the code is asked once per account and run (GUI: once per app session); these
  sessions are not logged out. Unattended runs of an account with 2FA fail with a clear error.
- **MediaFire session**: the token lasts 10 minutes; `GetClient` renews it when older than 5 and logs in again when
  the renewal fails.
- **SMB dates**: FILETIME values, UTC; `Smb.ToUtc` converts local dates and gives the UTC kind to unspecified ones.
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
- **Verification after the backup** (`BackupJobSettings.Verify`: `Never`, `FullBackups`, `AllBackups`; CLI
  `--verify`, GUI "Verification" card, 2026-11-27): the library `BackupJob` runs a `VerifyJob` on the backup it just
  wrote, as step `VerifyBackup`, forwarding its log, progress and files, so the GUI, the CLI and the automatic runs
  get it with no code of their own. `FullBackups` looks at the type actually run (an incremental that starts a new
  chain is full). Only a written backup (`Completed`/`CompletedWithWarnings`, not "no files to backup") is verified.
  A failed or cancelled verification fails the job (`Success` false, result `Error`/`Cancelled`, exit code 1/2,
  failure dialog of the automatic runs) but the backup stays `Completed` in the database: its data was written, the
  verification is reported apart. Chosen over "every N backups" or "every N days", which need a record of the last
  verification in the database (a migration) for little gain: with `FullBackupEveryDays` the full backups already
  come at regular intervals.

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
  repeated. Their user, password and OAuth options can be repeated too as `N=value` for the N-th path; a value without
  `N=` belongs to the first path (as before). Indexes are needed because the parser drops repeated values of an
  option (the same user on two destinations). Credentials in the path (`scheme://user:password@host`) still work.
  Too many values, an index without path and invalid OAuth urls are reported by `Arguments.Validate` like the parser
  errors. Restore and sync take one destination (`Arguments.Validate`). `--parallelFiles` (default 1) sets `ParallelFiles`. The CLI does not persist destination ids: the
  jobs match the recorded destinations by path. Built by `Arguments.GetDestinations` / `GetBackupDestinations`.
- **Restore does not retry a `MissingDestinationException`**: the destination does not appear by retrying.
- **Database of another backup**: every copy of the database records the destinations of its backup. When a
  destination has a database that the newest copy does not record, the destinations hold different backups and
  `DestinationSet.OpenDatabase` throws `ForeignDatabaseException` (both paths in the message) instead of reading the
  newest one. Databases without destination records (before multiple destinations) are not checked; a destination
  removed from a job and added back later is reported too (use an empty folder).
- **Free space**: backup and sync set `SpaceConfirmArgs.AvailableBytes` (sum of the destinations, null when one
  cannot report it) and `NeededBytes` (bytes to copy, less the deleted ones for sync; compression is not estimated)
  before the confirmation, with a log warning when it is not enough. The CLI adds it to the question (default "no"),
  the GUI, which confirms the job before starting it, asks again only then.
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

## CLI
- **Actions**: `BaseAction.RunJob` runs every job (log, errors, cursor), `WithCommonSettings` sets log level and log
  file for every action (`--log` works everywhere), the progress lines are shared.
- **Redirected output** (scripts, scheduled tasks, pipes): `Utility.Console` writes no progress lines and never
  touches the cursor or `WindowWidth`; with redirected input confirmations and the password are read as lines. The
  parser's usage text needs the console width, so it is printed only to a console.
- **Exit codes**: 0 success (also with warnings), 1 error, 2 cancelled (by the user or declining the confirmation),
  -1 invalid arguments. A verification with errors is a failure (`VerifyJob`: `Success` false, `ErrorMessage`).

## Setup
- **Uninstall** (`Setup/Mpbkpu.iss`, `[Code]`): after removing the program it asks whether to delete the settings
  and saved credentials of the user who uninstalls (`%LOCALAPPDATA%\MpbkpuGUI`: settings.json, secrets;
  `%LOCALAPPDATA%\Mpbkpu`: OAuth tokens, known_hosts). Default No, so a reinstall finds the jobs; nothing is deleted
  on silent uninstalls; other users of the computer keep their data; the backups are never touched.

## Automatic runs (GUI, 2026-11-27)
- **Scheduler of the operating system**, for the current user (no administrator rights, no stored password):
  Windows Task Scheduler (`Utility/WindowsTask`, task "Mpbkpu {job id}" from an XML definition: interactive token,
  `StartWhenAvailable`, no battery limits), systemd user timer on Linux (`Utility/SystemdTimer`,
  `~/.config/systemd/user/mpbkpu-{id}.timer/.service`, `Persistent=true`; cron was rejected: no D-Bus session, so no
  keyring with the saved passwords), launchd agent on macOS (`Utility/LaunchAgent`,
  `~/Library/LaunchAgents/com.mpbkpu.job.{id}.plist`, `launchctl bootstrap gui/{uid}`). Common base
  `Utility/JobScheduler` (`Current`, `Apply`, `Delete`). Runs happen only while the user is signed in (Linux: also
  with `loginctl enable-linger`).
- **Schedule** (`Job.Schedule`, `Models/JobSchedule`): days of the week and a time, for backups and synchronizations.
  `Days` is deserialized with `ObjectCreationHandling.Replace` (Newtonsoft would add the saved days to the default
  ones). The scheduler entry is written when the job is saved (before the settings: a failure keeps the page open) and
  removed when the job is deleted; the uninstaller removes the Windows tasks.
- **The entry starts the GUI executable without a window** (`mpbkpugui --run-job {id}`, `Utility/ScheduledRun`), not
  the CLI: the job, its credentials and the saved encryption password come from the GUI settings and the secret store
  of the user, so no secret ends up in the scheduler. An encrypted backup can be scheduled only with its password saved
  (asked when the schedule is saved). No confirmation handler: like the CLI with `-y`.
- **Results**: settings.json belongs to the GUI, which may be open, so a scheduled run writes only
  `%LOCALAPPDATA%\MpbkpuGUI\scheduled\{id}.json` (written aside and renamed) and the log `logs\{id}.log` (last run
  only). The job list merges the results newer than `LastRun` (`LastRunAutomatic`, "Automatic" button that opens the
  log) when it is shown and, through a `FileSystemWatcher`, while it is visible; failed runs are reported in one
  message. No system notification: the GUI reports them when opened.
- **Schedule page** (`Pages/SchedulePage`, "Scheduled" in the job list sidebar with the number of scheduled jobs,
  hidden where no scheduler is supported): every scheduled job, ordered by next run, with days and time, next run,
  result and date of the last run (automatic or by hand), the error of a failed automatic run, its log and the job
  settings. Results are merged when the page is shown and while it is visible.
- **One run per job** (`ScheduledRun.TryLock`): a lock file opened with `FileShare.None` and deleted on close, in any
  process and on every OS (a named mutex is thread-affine, named semaphores are Windows only). A scheduled run of a
  job already running ends at once; a run by hand of a job running by itself is refused with a message.

## Updates (GUI, 2026-11-27)
- **Source**: the GitHub API `releases/latest` of `sakya/Mpbkpu` (drafts and pre-releases excluded). The tag
  (`v0.2.0`) is compared with the assembly version (`SolutionInfo.proj` `<Version>`), missing parts as 0. The setup
  program is the asset `Mpbkpu-*-setup.exe`.
- **When**: quietly after the window opens (`AppSettings.CheckForUpdates`, default on, switch in the settings page; errors
  ignored), and with "Check for updates" in the settings page (the whole update section, release page and
  installation included, moved there from the about page). A newer release shows "Update available" in the job list
  sidebar, which opens the settings page.
- **Install** (`Utility/UpdateChecker`): only on Windows installations made by the setup program (`unins000.exe` next
  to the exe); elsewhere only the release page. The setup is downloaded to `%TEMP%\MpbkpuUpdate` from `github.com`
  over HTTPS and run only if its SHA-256 matches the `digest` GitHub publishes for the asset (no digest → refused).
  It runs with `/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART /update=1` and the GUI closes; the setup keeps
  the previous folder and install mode (UAC prompt for an all-users installation) and starts the GUI again (`[Run]`
  entry with `Check: IsUpdateFromApp`, as the original user). Leaving the settings page cancels a download. The CLI does
  not check for updates.

## Export and import of jobs (GUI, 2026-11-27)
- **Where**: "Export and import jobs" card in the settings page; all the jobs go to one JSON file
  (`Utility/JobTransfer`: `Version`, `ExportedAt`, `Jobs`), imported jobs are added to the existing ones (nothing is
  replaced or merged by name).
- **No secrets**: the file is the Newtonsoft serialization of the jobs, whose secrets are `[JsonIgnore]` (they live in
  `SecretStore`). Passwords, the encryption password and OAuth app secrets must be entered again; cloud services ask to
  sign in again on the new computer.
- **On import**: new job ids (they name secrets, logs, locks and scheduler entries), position ids kept (the backup
  databases record the destination of each volume with them), run history cleared, schedules turned off (an encrypted
  backup cannot run by itself before its password is saved again; the user turns the schedule on and the scheduler
  entry is created then), `RememberPassword` off.
- **Untrusted files**: `$type` names are needed for the job and position types, so a `SerializationBinder` accepts only
  types of the GUI and library assemblies; any other type, a file with no jobs or a newer `Version` is refused as
  "not a job export".

## GUI
- **Service picker**: a ComboBox; chips were tried (2026-11-27) and rejected by the user.
- **Switches, no check boxes**: options are `ToggleSwitch` with the label as `OnContent`/`OffContent` (beside the
  switch, like a check box) and `AutomationProperties.Name`; UI Automation toggles them with `TogglePattern`.
- **Themes**: `App.axaml` keeps one color set per theme variant in `ThemeDictionaries` (Dark: the original palette,
  Light); the brushes (`BgBrush`, `TextBrush`, ...) take their color with `DynamicResource`, and every page refers to
  colors and brushes with `DynamicResource`, so the theme changes without restarting. No fixed colors in pages or
  code: add a color to both dictionaries (code binds with `GetResourceObservable`). `AppSettings.Theme`
  (System, the default, Light, Dark) is applied by `App.ApplyTheme` at startup and by the settings page.
- **Settings page** (`Pages/SettingsPage`, "Settings" in the job list sidebar, 2026-11-27): the settings of the app,
  not of the jobs: theme, accent color, check for updates at startup. Every change is applied and saved at once (no
  OK/Cancel); choices are ComboBoxes with localized enum values (`Theme_*`, `Accent_*`), options are switches.
- **Accent color** (`AppSettings.Accent`, default System; settings page, 2026-11-27):
  `Utility/AccentPalette` replaces the `Accent*` colors of both theme dictionaries and the Fluent palette accent with
  colors derived from the system accent (`PlatformSettings.GetColorValues().AccentColor1`, followed through
  `ColorValuesChanged`): accent text moved towards white/black until 4.5:1 on the background, soft background mixed
  with the background, hover/pressed shades, button text white unless below 3:1 (then black, e.g. yellow accents).
  Systems that report no accent (alpha 0, some Linux desktops) keep the Mpbkpu blue, which is restored from the values
  read from App.axaml. The accent button shades are theme colors now (`AccentHoverColor`, `AccentPressedColor`,
  `AccentForegroundColor`), no longer fixed in the brushes.
- **Copy command** (job list): `MpbkpuLib.Utility.CommandLine` builds the CLI command of a backup or sync job from the
  same settings the GUI runs (`BackupJob.CreateSettings`, `SyncJob.CreateSettings`), in PowerShell syntax (single
  quotes, `&` before a quoted executable: `mpbkpu.exe` next to the GUI, else `mpbkpu` from the PATH), with `-y` and
  only the options that differ from the CLI defaults. No secret is written: an encrypted backup gets `-i`, passwords
  and OAuth secrets become `<password>`. With more destinations every credential value is `N=value`, so every job
  can be reproduced. `Tests/CommandLine.cs` parses the commands with the CLI `Arguments` (the test
  project references `Mpbkpu`), so a renamed option fails a test.
- **Backup browser**: double click on a directory of the file list selects it in the tree (its ancestors are
  expanded first); right click on a file, a directory or a tree node selects it (`ItemsList` on the right button
  press, the TreeView by itself) and offers "Download...": a folder picker, then
  `RestoreJob` with `RestoreJobSettings.RelativePath`, which restores that item (a directory with its content) into
  the folder with its name and without its parent directories, overwriting existing files and deleting nothing
  (the destination is not read). The job settings come from the job (destinations, password) as for verify.
- **Restore from the history** (2026-11-27): a "Restore" button on every backup asks for a local folder and a
  confirmation, then runs `RestoreJob` with `RelativePath = ""`: every file of that backup (its chain included) is
  restored into the folder with its relative path, overwriting existing files and deleting nothing. A full restore
  (`RelativePath` null, like the CLI) would make the folder identical to the backup and delete everything else in it:
  too dangerous with a folder picker. The download of the backup browser and this restore share
  `Utility/ItemRestore` (picker, spinner with steps, progress bar, files/bytes/time left and a cancel button; a
  cancelled restore keeps the files restored so far). The progress lives in the `Spinner` overlay
  (`ShowProgress`, `CancelRequested`: the button is shown while a handler is attached) instead of a dedicated page.
  The back arrow during a download or restore is the same as the cancel button (`ItemRestore.ConfirmLeave` in
  `OnNavigatingFrom`): both ask for a confirmation, cancel and keep the page to show the result, like the verify and
  backup pages.
- **Test connection** (`PositionDetail`, every source and destination): validates the fields, then creates the file
  manager from the typed settings, checks the folder (`DirectoryExists`) and reads the free space (optional). A
  missing folder is reported as success with a note (a backup creates it). Cloud services may open the browser to
  sign in: the test gives up after 3 minutes. Nothing is saved.
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
- **Exclusions** (`Job.Exclude`, backup and sync, 2026-11-27): one list of names, relative paths and wildcard
  patterns, matched by the library like the CLI `--exclude` (no separate lists for files and folders). Free text plus
  "Folders..."/"Files..." pickers, shown only for a local source, that add the picked items relative to the source
  (items outside it are reported, not added). Saved trimmed and distinct, since the CLI parser drops repeated values
  ("Copy command" writes one `-e` per item). No preview of what is excluded (the source may be remote).
- **File system types** in the job settings are sorted by their name in the current language; a new position is a
  local folder.
- **Windows animations with the custom title bar**: a Win32 window styles callback adds `WS_CAPTION` (DWM needs it for
  the open/minimize/maximize/restore animations) and removes `WS_SYSMENU` (native caption buttons);
  `WindowDecorations.BorderOnly` still hides the native caption. Same fix as Darsena (commit `1fdf4834`).

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
  `Legacy*` setters and removed at startup. The backup encryption password is still never stored (asked on each run).

## GUI
- **SingleWindow navigation unchanged** (pages animated in `Container`, back arrow in the TitleBar); the sidebar is
  only on the job list.
- **Cards with `ItemsControl`** instead of `ItemsList` for the job list and the history: `ItemsList` colors the row
  backgrounds and conflicts with the cards. `ItemsList` stays in the file browser (selection is needed there).
- **Centralized styling** in `App.axaml` through Fluent resource overrides and classes (`card`, `chip`, `h1`, `accent`,
  `danger`, `icon`, `nav`, `log`…).
- **Result of the last run** saved in the job (`LastResult`) on every run, also on error or cancellation.
- **Windows animations with the custom title bar**: a Win32 window styles callback adds `WS_CAPTION` (DWM needs it for
  the open/minimize/maximize/restore animations) and removes `WS_SYSMENU` (native caption buttons);
  `WindowDecorations.BorderOnly` still hides the native caption. Same fix as Darsena (commit `1fdf4834`).

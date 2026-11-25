# Project status

_Updated: 2026-11-25_

## Goal
Mpbkpu: a multi-platform backup utility with encryption and compression, on local and remote file systems
(FTP, SFTP, SMB, OneDrive, Dropbox, Mega, Nextcloud, MediaFire). Three projects: library `MpbkpuLib`,
CLI `Mpbkpu`, Avalonia GUI `MpbkpuGUI`, plus `Tests` (NUnit).

## Completed and verified
| Task | How it was verified |
|---|---|
| Line endings normalized (`.gitattributes`: `* text=auto eol=crlf`) | committed (`55e3a7c`) |
| .NET 10, NuGet packages updated, EF Core 10, Graph 6, FluentFTP 55, SSH.NET 2026 | solution build |
| Avalonia 12.2 + AvaloniaSingleWindow 1.2.0 + Material.Icons (replacing Projektanker/FontAwesome) | build + app started |
| Backup logic: critical, high, medium and low issues from the review fixed (see DECISIONS.md) | 45 non-remote tests pass (`Tests/Regressions.cs` + existing tests) |
| Backup chains: full backup at any time, `FullBackupEveryDays`, `KeepChains` (lib, CLI, GUI) | tests `TypeChangeStartsNewChain`, `KeepChainsDeletesOlderChains`, etc. |
| Encryption v2 with HMAC, key derived once per job, v1 files still readable | tests `TamperedOrWrongPasswordIsRejected`, `V1EncryptedDataCanBeDecrypted` |
| Locked files / files changed during the copy → saved warnings, `CompletedWithWarnings` result | tests `LockedFileIsSkippedWithWarning`, `FileChangedDuringCopyIsRetried` |
| GUI redesign (mockup: https://claude.ai/artifact/HpYrM1fJ6XE4of8AhhtrrS) | screenshot of every page: job list, running, settings, history, verify, browse, delete, about, confirmation dialog |
| Delete backup page implemented (it was empty) | deletion run from the GUI, history reloaded |
| Position passwords and `OauthAppSecret` moved from `settings.json` to the OS secure storage (`SecretStore`), automatic migration of old files | scratch test: legacy file loaded, secrets moved, `settings.json` without secrets, reload and `Job.Copy` keep them |
| Folder synchronization fixed: unchanged files no longer deleted and copied again, hidden/excluded/unreadable items never deleted from the destination, `Exclude` applied, file/directory swap, temp file + rename, `Result` with warnings/cancellation | `Tests/Sync.cs` (7 tests, all failing on the old code) + 52 non-remote tests |
| MEGA file manager fixed: real rename, upload through a bounded pipe (`UploadStream`, no hangs, incomplete uploads cancelled), node list cached, paths always from the root, no duplicate folders/files, proper not-found exceptions | `Tests/Utilities.cs` (upload stream offline); NOT tested against a real MEGA account |
| MediaFire file manager fixed: rename with `FileUpdate`, upload through `UploadStream` (Replace on duplicates), streamed downloads with HTTP status check, consistent full paths for `mediafire://user:password/path`, no duplicate folders, not-found exceptions | build only; NOT tested against a real MediaFire account |
| Nextcloud fixed: directory listing (hrefs converted to paths, folder itself skipped, `Depth: 1`), upload status checked and streamed through `UploadStream`, downloads on a live `HttpClient`, no 100 s timeout, OAuth token refreshed before expiry and saved after every refresh, token requests in the POST body, 401 retried once, escaped URLs | `Tests/Utilities.NextcloudPathsAreEscapedAndParsed`; NOT tested against a real server |
| Local file systems: only symlinks/junctions are skipped (cloud placeholders like OneDrive were skipped as reparse points), broken file links skipped, dates in UTC (old local dates in backup databases still match), read-only files and directories containing junctions can be deleted, Windows path detection anchored | `Tests/Utilities` (`LocalReadOnlyFilesCanBeDeleted`, `LocalDatesAreUtcAndLinksAreNotFollowed`, `ModificationTimesSavedAsLocalTimeStillMatch`) |
| FTP/SFTP fixed: credentials given apart (GUI) were ignored (inverted condition), GUI form `ftp:///host/path` rejected, GUI sync passed `Path` without scheme (remote targets became local folders), full paths consistent with the job root, dot-files hidden, SFTP rename always failed, SFTP listing recursed into `.`, SFTP default port 22, SFTP host key trust-on-first-use (`known_hosts`), FTP certificates validated, FTP upload reply checked, UTC dates | `Tests/Utilities.FtpAndSftpUseTheGivenCredentials`; NOT tested against real servers |
| OneDrive fixed: token taken from MSAL for every request (was fixed at sign-in, jobs failed after ~1 hour), full paths consistent with the job root, root addressed by id, items by path then by id, upload through `UploadStream` (result checked, empty files with a simple PUT), no duplicate folders, not-found exceptions, case-insensitive | build + 67 offline tests; Graph URLs checked offline; NOT tested against a real account |
| Dropbox fixed: full paths consistent with the job root, root addressed as "" (Dropbox rejects "/"), chunked upload through `UploadStream` with one buffered request per 4 MB chunk (no fire-and-forget chunk switch), overwrite mode, not-found exceptions, no duplicate-folder errors, case-insensitive | build + 67 offline tests; NOT tested against a real account |
| Build warnings removed (0 warnings): `FindControl(...)!` in the GUI, nullable fixes; two upload tests never asserted (NUnit 5 `ThrowsAsync` not awaited), `UploadStream` now always wraps upload errors in `IOException` | build + tests |
| SMB fixed: listing returned only folders (files never seen), closing an upload could truncate it (abort before the last block was read), rename moved to an absolute path (EzSmb wants a path relative to the parent), folders deleted recursively, existing files deleted before writing, upload result checked, domain of `DOMAIN\user` parsed wrong, URL credentials without domain accepted, full paths consistent with the job root (EzSmb returns IP addresses) | `Tests/Utilities.SmbAddressesAreParsed`; EzSmb behaviour read from its source; NOT tested against a real server |
| GUI: the source/destination password typed in the job settings was never saved (`PositionDetail.Value` did not copy it): jobs on MediaFire, MEGA, FTP, SFTP, SMB, Nextcloud with password failed ("Value cannot be null (password)") | build; existing jobs need the password typed again |
| MediaFire login: "Cannot get security code" because the login page now starts with an empty `security` field. Fixed in MediaFireApi (`Client.GetSecurityCode`, first non-empty value), version 0.2.2; Mpbkpu references 0.2.2 | backup to MediaFire with a real account done by the user (login, upload, history browse); 0.2.2 only in the local NuGet cache until published |
| Time left: `Utility/EtaEstimator` (bytes + fixed cost per item, speed over the last 30 s, no estimate in the first 3 s, shown as `--:--:--`), one estimate per phase (sync and restore deletions no longer counted in the copy time, deletions estimated by count); GUI percentage by files when there are no bytes (was NaN) | `Tests/Utilities.EtaFollowsTheRecentSpeedAndCountsItems` |
| Browse page (History): files directly in the backed up folder were never shown because the tree had no root node; `BackupFile.GetDirectoryTree` now returns the backed up folder as root (path "") and the page selects it on open | `Regressions.DirectoryTreeHasTheBackedUpFolderAsRoot`; confirmed by the user in the GUI (root selection moved to the UI thread) |
| `IFileManager.GetSpace` (free + total, `StorageSpace`): local (drive/mount point), FTP (`AVBL`, free only), SFTP (`statvfs`), MEGA, MediaFire, OneDrive, Dropbox, Nextcloud (`quota-available-bytes`/`quota-used-bytes`); null when unknown (SMB, servers without the extension) | local tested; Nextcloud parsing tested offline; remote ones NOT tested against real accounts |
| Destination space in the GUI: job list row with an inline bar (yellow from 80%, red from 95% used) and text; local destinations read live, remote ones show the last value saved in the job (`Job.DestinationSpace`) with a refresh button; backup and sync jobs emit `BaseJob.SpaceChanged` (read at start and end, estimated from the bytes written/deleted in between), shown on the running page and saved in the job | screenshots: job list (local/FTP cached/unknown) and a local backup run (space updated during the run, saved at the end) |
| Message dialogs: buttons right-aligned, secondary before primary; confirmations with the warning icon (delete, abort) use the danger style; Button1 `IsCancel` bound to the wrong button fixed | screenshots of the run and delete-job confirmations |
| GUI: backup encryption password can be remembered (checkbox in the password dialog, off by default) in the OS secure storage (`SecretStore`, key `{jobId}.EncryptPassword`); saved password used without asking; "Forget password" in the job settings; a saved password that turns out wrong (`InvalidPasswordException`) is forgotten with a message; settings are saved even without a secure storage (`AppSettings.SecretStoreAvailable`, the checkbox is hidden) | `Regressions.WrongPasswordRaisesInvalidPasswordException`; save/load round trip checked in a scratch program; on screen: dialog, run without dialog after restart, settings status, forget then asked again |
| Job settings: file system types sorted by their localized name; "Local folder" always selected for a new position (the binding was not refreshed when the type equals the model default) | UI Automation: new backup and new sync select "Cartella locale", list order checked |
| Passwords in `scheme://user:password` paths masked in job logs and in the settings saved in the backup database (`BaseJob.SerializeSettings`) | `Tests/Utilities.CredentialsAreMasked` |
| Window style with `WS_CAPTION` for the Windows animations (same fix as Darsena) | Win32 style read from the running window; custom title bar intact; maximize/restore/close work |

## Uncommitted changes
- Sync fix: `MpbkpuLib/Jobs/SyncDirectoryJob.cs`, `Abstracts/BaseJob.cs` (`Exclude`, `IsExcluded`, `ToPortablePath`),
  `Abstracts/BaseJobResult.cs` (`Result` moved here from `BackupJobJobResult`), GUI `Pages/JobRunningPage.axaml.cs`,
  `Tests/Sync.cs` (new).
- MEGA fix: `FileManagers/Mega.cs`, `Utility/UploadStream.cs` (new), `Tests/Utilities.cs` (new).
- MediaFire fix: `FileManagers/MediaFire.cs`.
- Nextcloud fix: `FileManagers/Nextcloud.cs`, `NextcloudClient/Client.cs`, `Client.Auth.cs`, `File.cs`, `Folder.cs`.
- OneDrive: `FileManagers/OneDrive.cs`.
- Dropbox: `FileManagers/Dropbox.cs`.
- SMB: `FileManagers/Smb.cs`, `FileManagerFactory.cs`.
- Warnings: GUI `Controls/*`, `Dialogs/PasswordDialog`, `Pages/*`, `Views/MainWindow`, `MpbkpuLib/BackupFile.cs`.
- FTP/SFTP: `FileManagers/Ftp.cs`, `Sftp.cs`, `FileManagerFactory.cs`, GUI `Pages/JobRunningPage.axaml.cs` (sync uses
  `FullPath`).
- Local file systems: `Abstracts/BaseLocalFileSystem.cs`, `FileManagerFactory.cs` (anchored regex),
  `Utility/Files.SameModificationTime` (used by `BackupJob`/`RestoreJob`), UTC dates set by sync and restore.
- Credentials masking: `BaseJob.MaskCredentials`/`SerializeSettings`, used by every job.
- Memory files.

## Current state
Synchronization and all the file managers (MEGA, MediaFire, Nextcloud, OneDrive, Dropbox, FTP/SFTP, SMB, local) fixed; 0 build
warnings; 67 offline tests pass. The remote services are not tried against
real accounts. Waiting for a commit.

## Open issues and attempts so far
- `Tests/FileManagers` tests use real credentials (`filemanagers-settings.json`) and hang (OneDrive waits for an
  interactive login) → excluded with `--filter "FullyQualifiedName!~FileManagers"`. Not solved.
- Not verified on screen: the warnings box in the history (no test backup with warnings), the password dialog,
  windows narrower than 1180×780, the Windows animations themselves (they cannot be seen in a screenshot).
- Computer use cannot control the development exe (not an installed app): GUI checks use PrintWindow + UI Automation
  (see AGENTS.md).
- Backups created before the fix with relative source paths have wrong `RelativePath` values in the database:
  they cannot be repaired automatically.

## Next step
Commit. Then try MEGA, MediaFire, Nextcloud, OneDrive, Dropbox, FTP, SFTP and SMB with real accounts (backup + sync: upload, rename of `.mpbkpu-tmp`, database
copy, download), and apply
`UploadStream` to Dropbox (same blocking `WriterStream`).

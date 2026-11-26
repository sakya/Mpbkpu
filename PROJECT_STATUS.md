# Project status

_Updated: 2026-11-26_

## Goal
Mpbkpu: a multi-platform backup utility with encryption and compression, on local and remote file systems
(FTP, SFTP, SMB, OneDrive, Dropbox, MEGA, Nextcloud, MediaFire). A backup can be split among more destinations.
Projects: library `MpbkpuLib`, CLI `Mpbkpu`, Avalonia GUI `MpbkpuGUI`, `NextcloudClient`, `Tests` (NUnit), installer
in `Setup/`. MediaFire uses the author's `MediaFireApi` NuGet package (repository `../MediaFireApi`).

## Completed and verified
| Area | What | How it was verified |
|---|---|---|
| Platform | .NET 10, Avalonia 12.2, AvaloniaSingleWindow 1.2.0, Material.Icons, EF Core 10, current NuGet packages | build, app started |
| Backup logic | Review issues fixed, chains (`FullBackupEveryDays`, `KeepChains`), encryption v2 with HMAC, locked/changed files as warnings | `Tests/Regressions.cs` |
| Sync | Unchanged files no longer deleted and recopied, hidden/excluded/unreadable items never deleted, `Exclude` applied, temp file + rename, result with warnings/cancellation | `Tests/Sync.cs` |
| File managers | Rename, uploads (`UploadStream`), paths consistent with the job root, root folders, not-found exceptions, credentials, tokens, TLS/host keys, UTC dates (details in DECISIONS.md) | offline tests in `Tests/Utilities.cs`; real account: **MediaFire only** |
| MediaFire login | "Cannot get security code" fixed in MediaFireApi 0.2.2 (NuGet) | user's backup to MediaFire |
| Credentials | Passwords and OAuth secrets in the OS secure storage (`SecretStore`); passwords in `scheme://user:password` paths masked in logs and backup database | scratch round trip, `CredentialsAreMasked` |
| Encryption password | Optional "remember", "Forget password", wrong saved password forgotten (`InvalidPasswordException`) | tests + on screen |
| Free space / time left | `IFileManager.GetSpace`, space read/estimated during jobs; `EtaEstimator` | tests + screenshots |
| Multiple destinations (library) | Files split among the destinations (fewest assigned bytes), database read from the newest copy and written to all, legacy databases migrated (`MultipleDestinations`), list/restore/verify/delete/`KeepChains` on every destination, `MissingDestinationException`, parallel copy engine (`ParallelFiles`, workers) | 13 tests in `Tests/Regressions.cs` (local folders) |
| Multiple destinations (CLI) | Repeatable `-d` / `-b`, `--parallelFiles`, restore and sync take one destination, restore does not retry a missing destination | end to end in a real console on 3 folders (backup, list, verify, restore identical, missing destination, delete) |
| Multiple destinations (GUI) | Other destinations and "files copied at the same time" in the job settings, persisted position ids, secrets per destination, space per destination (job list, running page), history/verify/delete on every destination | on screen with a job on 3 local folders (PrintWindow + UI Automation); verify of a MediaFire backup confirmed by the user |
| GUI | Redesign, delete page, history browser, dialogs, title bar, Windows animations (`WS_CAPTION`, not seen on screen) | screenshots; user |
| Setup | `Setup/build.bat` + `Setup/Mpbkpu.iss` | installer compiled, published GUI started (installer not run) |

Tests: 94 non-remote tests pass (`dotnet test Tests/Tests.csproj --filter "FullyQualifiedName!~FileManagers"`), run
after every change below. Clean build: 0 warnings, 0 errors.

## Current state
Committed (`60e8f3e fix #61`): multiple destinations, the two build warning fixes, parallel copies on local file
systems (`BaseLocalFileSystem.MaxParallelTransfers` has no limit, test `LocalFileSystemsCopyFilesAtTheSameTime`) and
the list of the files being copied on the running page (`FileCompleted`; seen on screen: 4 of 8 files copied at the
same time, list emptied at the end). Every remote file manager is still limited to 1, so a backup with a remote
source or only remote destinations copies one file at a time.

Committed (`e3e8012`, `3339fb1`), all confirmed working on screen by the user (2026-11-26):
- light theme (`ThemeDictionaries` in `App.axaml`, `AppSettings.Theme`, sidebar button System/Light/Dark);
- "Copy command" in the job list (`MpbkpuLib.Utility.CommandLine`, tests in `Tests/CommandLine.cs`; a command of the
  same form also run in a real console);
- the backup browser: double click opens a directory, right click selects the row and downloads a file or directory
  (`RestoreJobSettings.RelativePath`, test `RestoreSingleItemKeepsOtherFiles`).

## Open issues and attempts so far
- Remote file managers other than MediaFire never tried with real accounts, also as additional destinations.
- Parallel copies only on local file systems (tests); remote file managers limited to 1, never tried in parallel.
- "Value cannot be null (password)" on verify of a MediaFire backup (2026-11-26): after the user recreated the job
  (they had deleted `settings.json`) it works. Probable cause, not proven: the GUI screen test overwrote
  `settings.json`/`secrets` while the user's GUI was using them. The code passes the job password to verify.
- CLI: crash when the output is redirected, `--log` ignored by list/list content/verify, exit code always 0 (TODO.md).
- `Tests/FileManagers` need real credentials and hang on the OneDrive login: always excluded.
- Not verified on screen: warnings box in the history, windows narrower than 1180×780, the Windows animations.
- Backups made before the fixes: wrong `RelativePath` for relative source paths (not repairable); modification
  dates restored shifted by the UTC offset (change detection is not affected).

## Next step
Run a backup and a sync on each remote service used in practice with a real
account (upload, `.mpbkpu-tmp` rename, database copy, history, verify, restore), also as an additional destination
of a local backup.

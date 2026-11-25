# Project status

_Updated: 2026-11-25_

## Goal
Mpbkpu: a multi-platform backup utility with encryption and compression, on local and remote file systems
(FTP, SFTP, SMB, OneDrive, Dropbox, MEGA, Nextcloud, MediaFire). Projects: library `MpbkpuLib`, CLI `Mpbkpu`,
Avalonia GUI `MpbkpuGUI`, `NextcloudClient`, `Tests` (NUnit), installer in `Setup/`. MediaFire uses the author's
`MediaFireApi` NuGet package (repository `../MediaFireApi`).

## Completed and verified
| Area | What | How it was verified |
|---|---|---|
| Platform | .NET 10, Avalonia 12.2, AvaloniaSingleWindow 1.2.0, Material.Icons, EF Core 10, current NuGet packages | build, app started |
| Backup logic | Review issues fixed, chains (`FullBackupEveryDays`, `KeepChains`), encryption v2 with HMAC, locked/changed files as warnings | `Tests/Regressions.cs` |
| Sync | Unchanged files no longer deleted and recopied, hidden/excluded/unreadable items never deleted, `Exclude` applied, temp file + rename, result with warnings/cancellation | `Tests/Sync.cs` (failed on the old code) |
| File managers | MEGA, MediaFire, Nextcloud, OneDrive, Dropbox, FTP, SFTP, SMB, local: rename, uploads (`UploadStream`), paths consistent with the job root, root folders, not-found exceptions, credentials, tokens, TLS/host keys, UTC dates (details in DECISIONS.md) | offline tests in `Tests/Utilities.cs`; real account: **MediaFire only** (backup + history browse by the user) |
| MediaFire login | "Cannot get security code" (empty `security` field on the login page) fixed in MediaFireApi 0.2.2, published on NuGet and referenced by Mpbkpu | user's backup to MediaFire |
| Credentials | Source/destination passwords and OAuth secrets in the OS secure storage (`SecretStore`), migrated from old `settings.json`; GUI job settings did not save the typed password (fixed); passwords in `scheme://user:password` paths masked in logs and backup database | scratch round-trip test, `CredentialsAreMasked` |
| Encryption password | Optional "remember" in the password dialog (OS secure storage), "Forget password" in the job settings, wrong saved password forgotten (`InvalidPasswordException`); settings saved even without a secure storage | `WrongPasswordRaisesInvalidPasswordException`, scratch round trip, on screen (dialog, run without dialog after restart, forget, asked again) |
| Free space | `IFileManager.GetSpace` (free + total); job list bar with refresh for remote destinations; space read/estimated during backup and sync and saved in the job | local test; screenshots of job list and of a local backup run |
| Time left | `EtaEstimator` (recent speed, cost per file, one estimate per phase, `--:--:--` until known); GUI percentage by files when there are no bytes | `EtaFollowsTheRecentSpeedAndCountsItems` |
| GUI | Redesign; delete backup page; history browser with root node; message dialog buttons ordered (danger style for destructive confirmations); `BackupOutline` icon for backups; file system types sorted, "Local folder" default; sync settings title; title bar does not move; 0 build warnings | screenshots via PrintWindow + UI Automation; history root confirmed by the user |
| Windows animations | Window style with `WS_CAPTION` | Win32 style read from the running window (the animation itself not seen) |
| Setup | `Setup/build.bat` (self-contained win-x64 publish) and `Setup/Mpbkpu.iss` (all users / current user) | build run, installer compiled, published GUI started (installer not run) |

Tests: 73 non-remote tests pass (`dotnet test Tests/Tests.csproj --filter "FullyQualifiedName!~FileManagers"`).

## Current state
Working on **multiple destinations per backup** (files split among the destinations, database in all of them,
parallel copies; design in DECISIONS.md, phases in TODO.md). Phases 1 to 5 done, not committed:
- Phase 1: settings (`BackupDestination`, `Destinations`, `ParallelFiles`, old single-destination keys still read),
  validation, table `Destinations` and `BackupVolume.DestinationId` (migration `MultipleDestinations`).
- Phase 2: `BackupJob` splits the files among the destinations (sequential) through `DestinationSet`, reads the
  newest database and writes it to every destination (`BackupFile.SaveBackupFile`), space per destination
  (`SpaceEstimate`).
- Phase 3: parallel copies with `ParallelFiles` (reader + workers on a `Channel`), `IFileManager.MaxParallelTransfers`,
  database, progress and logging thread-safe, `NextcloudClient` token refresh under a lock. Every file manager has
  limit 1 for now, so the copies run one at a time.
- Phase 4: list, list content, restore, verify and delete read every destination (`BackupDestinations`), find each
  volume in its destination, report a missing one (`MissingDestinationException`); delete and `KeepChains` work on
  all destinations.
- Phase 5: CLI with repeatable `-d` (backup) and `-b` (other actions), `--parallelFiles`; restore and sync refuse
  more than one destination; restore no longer retries a missing destination. Verified end to end in a real console
  (backup to 3 folders, list, verify, restore identical to the source, missing destination reported without retries,
  delete from every folder).
- Verified by the tests `LegacySingleDestinationSettingsAreRead`, `InvalidDestinationSettingsAreRejected`,
  `DatabaseBeforeMultipleDestinationsIsMigrated`, `FilesAreSplitAmongDestinationsAndDatabaseIsInAll`,
  `LegacyVolumesBelongToTheDestinationOfTheDatabase`, `DatabaseCopyFailureOnOneDestinationIsAWarning`,
  `ParallelBackupRestoresTheSource`, `ParallelProgressReachesTheTotal`, `FailedParallelBackupLeavesNoFiles`,
  `ParallelBackupCanBeCancelled`, `BackupsSplitAmongDestinationsAreRestoredVerifiedAndDeleted`,
  `MissingDestinationIsReported`, `KeepChainsDeletesOlderChainsInEveryDestination` and the existing suite (local
  folders only).
- The GUI still passes one destination only (phase 6); its history page (`BackupOpenPage`) opens the database of
  the job destination directly.
- CLI problems found while testing (TODO.md): crash when the output is redirected, `--log` ignored by list, list
  content and verify, exit code always 0.

## Open issues and attempts so far
- Remote file managers other than MediaFire never tried with real accounts: the fixes are verified only offline
  (unit tests, library source/docs, URLs built offline).
- `Tests/FileManagers` need real credentials (`filemanagers-settings.json`) and hang on the OneDrive interactive
  login: always excluded with the filter above.
- Not verified on screen: warnings box in the history, windows narrower than 1180×780, the Windows animations.
- Backups made before the fixes: wrong `RelativePath` for relative source paths (not repairable); modification
  dates restored shifted by the UTC offset (change detection is not affected).

## Next step
Phase 6 of multiple destinations: GUI (destination list with persisted ids, secrets per destination, space per
destination, parallel files setting, history/verify/delete pages on every destination).

Afterwards: try a backup and a sync on each remote service with a real account (upload, rename of `.mpbkpu-tmp`, database copy,
history, download/restore), starting with the ones used in practice.

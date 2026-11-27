# Project status

_Updated: 2026-11-27_

## Goal
Mpbkpu: a multi-platform backup utility with encryption and compression, on local and remote file systems
(FTP, SFTP, SMB, OneDrive, Dropbox, MEGA, Nextcloud, MediaFire, pCloud). A backup can be split among more destinations.
Projects: library `MpbkpuLib`, CLI `Mpbkpu`, Avalonia GUI `MpbkpuGUI`, `NextcloudClient`, `Tests` (NUnit), installer
in `Setup/`. MediaFire uses the author's `MediaFireApi` NuGet package (repository `../MediaFireApi`), the CLI the
author's `CmdLineArgsParser` (`../CmdLineArgsParser`).

## Completed and verified
| Area | What | How it was verified |
|---|---|---|
| Platform | .NET 10, Avalonia 12.2, AvaloniaSingleWindow 1.2.0, Material.Icons, EF Core 10 | build, app started |
| Backup logic | Chains (`FullBackupEveryDays`, `KeepChains`), encryption v2 with HMAC, locked/changed files as warnings, `Exclude` honoured, free space in the confirmation, `ForeignDatabaseException` (destinations holding different backups), verify with errors is a failure | `Tests/Regressions.cs` |
| Sync | Unchanged files kept, hidden/excluded/unreadable items never deleted, temp file + rename | `Tests/Sync.cs` |
| Multiple destinations | Library, CLI, GUI; parallel copy engine; local file systems copy in parallel (no `MaxParallelTransfers` limit) | 13+ regression tests; CLI end to end on 3 folders; GUI on screen (4 of 8 files copied at once) |
| CLI | Works with redirected output, exit codes (0 ok, 1 error, 2 cancelled, -1 invalid arguments), `--log` for every action, credentials per destination as `N=value`, actions on `BaseAction.RunJob` | `Tests/CommandLine.cs`; by hand in a real console (each exit code, redirected output, `--log`, `--exclude`) |
| Credentials | OS secure storage (`SecretStore`), passwords masked in logs and database | tests |
| MediaFire | Backup and verify with the user's account (MediaFireApi 0.2.2) | user |
| GUI | Light/dark theme, "Copy command", backup browser (double click, right click download), files being copied, history result icons | on screen by the user (2026-11-26/27) |
| Setup | `Setup/build.bat` + `Setup/Mpbkpu.iss` | installer compiled (not run) |

Tests: 112 pass with `dotnet test Tests/Tests.csproj` (`FileManagers` tests on real accounts are explicit, skipped
without `filemanagers-settings.json`). Clean build: 0 warnings, 0 errors.

## Current state
Committed up to `54add34` (2026-11-27). Implemented but **not verified on a real service or on screen**:
- service options (offline tests only): FTP `?tls=required`/`?fingerprint=`, SFTP key passphrase and `?hostkey=`,
  MEGA modification date on upload and 2FA code prompt, MediaFire session renewal, SMB dates as UTC;
- pCloud file manager (OAuth, US/EU server), OAuth browser login shared with Dropbox (`Utility/OAuthBrowserLogin`),
  account paths unified in `FileManagerFactory.GetAccount`;
- compression on Dropbox (upload session) and Nextcloud (chunked transfer encoding) through `UploadStream` of unknown
  length + `FileSystemStream.EndOfData`; SMB no longer claims compression; job settings refuse unsupported compression;
- GUI not seen on screen: switches instead of check boxes, free space dialog, FTP/SFTP path hints, compression error
  in the job settings.

Not committed: the uninstaller asks whether to delete settings and credentials (`Setup/Mpbkpu.iss`, `[Code]`);
script compiled, uninstall never run.

## Open issues and attempts so far
- Remote file managers other than MediaFire never tried with real accounts (also as additional destinations).
- Remote file managers copy one file at a time (`MaxParallelTransfers` 1), never tried in parallel.
- pCloud API details reconstructed from the JS SDK and rclone (docs.pcloud.com pages arrive truncated): `mtime` on
  upload, case sensitivity and the EU server are assumptions.
- Nextcloud chunked uploads may be refused by some reverse proxies (assumption, not tried).
- "Value cannot be null (password)" on a MediaFire verify (2026-11-26) disappeared after recreating the job; probable
  cause, not proven: a GUI screen test overwrote `settings.json`/`secrets` while the user's GUI was open.
- Not verified on screen: warnings box in the history, windows narrower than 1180×780, Windows animations.
- Backups made before the fixes: wrong `RelativePath` for relative source paths (not repairable); modification
  dates restored shifted by the UTC offset (change detection not affected).

## Next step
Commit the uninstaller change, then try with a real account each service used in practice (backup, sync, history,
verify, restore, also compressed and as an additional destination), starting from the ones changed on 2026-11-27.

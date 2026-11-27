# Project status

_Updated: 2026-11-27_

## Goal
Mpbkpu: a multi-platform backup utility with encryption and compression, on local and remote file systems
(FTP, SFTP, SMB, OneDrive, Dropbox, MEGA, Nextcloud, MediaFire, pCloud). A backup can be split among more destinations.
Projects: library `MpbkpuLib`, CLI `Mpbkpu`, Avalonia GUI `MpbkpuGUI`, `NextcloudClient`, `Tests` (NUnit), installer
in `Setup/`, CI in `.github/workflows/tests.yml`. MediaFire uses the author's `MediaFireApi` NuGet package (repository
`../MediaFireApi`), the CLI the author's `CmdLineArgsParser` (`../CmdLineArgsParser`).

## Completed and verified
| Area | What | How it was verified |
|---|---|---|
| Platform | .NET 10, Avalonia 12.2, AvaloniaSingleWindow 1.2.0, Material.Icons, EF Core 10 | build, app started |
| Backup logic | Chains (`FullBackupEveryDays`, `KeepChains`), encryption v2 with HMAC, locked/changed files as warnings, `Exclude` honoured, free space in the confirmation, `ForeignDatabaseException`, verify with errors is a failure, verification after the backup (`Verify`, CLI `--verify`) | `Tests/Regressions.cs` |
| Sync | Unchanged files kept, hidden/excluded/unreadable items never deleted, temp file + rename | `Tests/Sync.cs` |
| Multiple destinations | Library, CLI, GUI; parallel copy engine; local file systems copy in parallel | regression tests; CLI end to end on 3 folders; GUI on screen |
| CLI | Redirected output, exit codes (0 ok, 1 error, 2 cancelled, -1 invalid arguments), `--log`, credentials per destination as `N=value` | `Tests/CommandLine.cs`; by hand in a real console |
| Credentials | OS secure storage (`SecretStore`), passwords masked in logs and database | tests |
| MediaFire | Backup and verify with the user's account (MediaFireApi 0.2.2) | user |
| GUI | Light/dark theme, "Copy command", backup browser, files being copied, history result icons, exclusions | on screen by the user (2026-11-26/27) |
| Setup | `Setup/build.bat` (restore + clean per runtime before publish) + `Setup/Mpbkpu.iss` | build.bat run, published exe starts; installer compiled, not run |

Tests: 117 pass locally with `dotnet test Tests/Tests.csproj` (~8 min; `FileManagers` tests are explicit). Clean
build: 0 warnings, 0 errors.

## Current state
Everything is committed and pushed (`main` = `origin/main` = `f2a03cc`). Implemented in the 2026-11-27 session,
**build passes but not seen on screen**:
- Update check (`Utility/UpdateChecker`, "Updates" card in the settings page, "Update available" in the sidebar,
  `Setup/Mpbkpu.iss` restarts the GUI): check, download and SHA-256 verification tried against the real v0.1.0 release.
  A real update never tried.
- Restore of a whole backup from the history (`Utility/ItemRestore`): `Regressions.RestoreWholeBackupIntoFolderKeepsOtherFiles`.
- "Test connection" in the job settings, progress/cancel for download and restore (`Controls/Spinner`); not tried with
  remote services.
- Automatic runs (`Utility/JobScheduler`, `WindowsTask`, `SystemdTimer`, `LaunchAgent`, `ScheduledRun`, `--run-job`):
  tried on Windows with a scratch program (task XML created and removed, headless run, results merge, lock). Not
  tried: a task started by Windows, Linux and macOS.
- Schedule page (`Pages/SchedulePage`), settings page (`Pages/SettingsPage`: theme, accent, updates, export/import),
  accent color following the system (`Utility/AccentPalette`, contrast checked numerically).
- Verification card in the job settings (logic tested, see above).
- Export/import of jobs (`Utility/JobTransfer`): round trip tried with a scratch program (no secrets, new job ids,
  position ids kept, history cleared, schedule off, foreign `$type`/empty/newer files refused). File pickers not tried.
- CI: the first run (`23d529c`) failed on `Utilities.LocalDatesAreUtcAndLinksAreNotFollowed`: broken file links were
  listed (the runner can create file links, this PC cannot). Fixed in `f2a03cc` (`BaseLocalFileSystem` resolves file
  links to their target, skips broken ones); its CI run was in progress at the end of the session: **not confirmed**.

Older items not verified on a real service or on screen: FTP/SFTP/MEGA/MediaFire service options, pCloud, compression
on Dropbox/Nextcloud, switches/free space dialog/path hints in the GUI.

## Open issues and attempts so far
- `SolutionInfo.proj` `<Version>` went from 0.3.0.0 back to 0.2.0.0 in `23d529c` (not done by the agent; maybe on
  purpose to try the update check against v0.3.0): to check before the next release.
- Remote file managers other than MediaFire never tried with real accounts; remote copies are one file at a time.
- pCloud API details are assumptions (`mtime`, case sensitivity, EU server); Nextcloud chunked uploads may be refused
  by some proxies (not tried).
- "Value cannot be null (password)" on a MediaFire verify (2026-11-26) disappeared after recreating the job; probable
  cause, not proven: a GUI screen test overwrote `settings.json`/`secrets` while the user's GUI was open.
- Backups made before the fixes: wrong `RelativePath` for relative source paths (not repairable); modification
  dates restored shifted by the UTC offset (change detection not affected).

## Next step
Check the CI run of `f2a03cc` on GitHub (Actions tab); if it fails, read the log of the link test. Then check the
version in `SolutionInfo.proj` and look at the new pages on screen (settings, schedule, verification card, export/import).

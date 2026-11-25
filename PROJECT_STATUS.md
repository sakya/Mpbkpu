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
| Window style with `WS_CAPTION` for the Windows animations (same fix as Darsena) | Win32 style read from the running window; custom title bar intact; maximize/restore/close work |

## Uncommitted changes
- GUI: `App.axaml` (theme), `Views/MainWindow.*` (also the window styles callback), `Controls/*` (TitleBar,
  JobListItem, OkCancelButtons, PositionDetail, ErrorContainer, Spinner), `Pages/*` (all), `Dialogs/*`,
  `Models/BackupJob.cs`, `Models/BackupOpenPageModel.cs`, `Abstracts/Job.cs` (`LastResult`),
  `Converters/EnumToLocalizedStringConverter.cs` (new), `DateTimeConverter.cs`, `Assets/i18n/*.json`,
  `MpbkpuGUI.csproj` (Avalonia.Fonts.Inter).
- Library: `BackupJobJobResult.Result`, job settings passwords and `OAuth2Settings.AppSecret` marked `[JsonIgnore]`.
- Project memory files: `PROJECT_STATUS.md`, `DECISIONS.md`, `AGENTS.md`, `TODO.md`.
- Everything builds; the 45 non-remote tests pass with these changes.

## Current state
GUI redesign applied and checked on screen, alignment fixes (checkboxes, numeric inputs) verified, window
animation fix applied. Waiting for a commit.

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
Check the Windows animations by hand (minimize, maximize, restore), then commit the GUI redesign and the pending
library changes.

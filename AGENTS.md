# Notes for agents
Nella chat parla in italiano

## Structure
- `MpbkpuLib/`: logic (jobs in `Jobs/`, file managers in `FileManagers/`, EF Core SQLite database in `BackupFile.cs`).
- `Mpbkpu/`: CLI (`Arguments.cs`, `Actions/`).
- `MpbkpuGUI/`: Avalonia 12 + AvaloniaSingleWindow GUI (pages in `Pages/`, theme in `App.axaml`, texts in `Assets/i18n/`).
- `Tests/`: NUnit; `Regressions.cs` holds the regression tests of the backup logic.

## Commands
- Build: `dotnet build Mpbkpu.sln`
- Tests (without the remote FileManagers ones, ~2-5 min): `dotnet test Tests/Tests.csproj --filter "FullyQualifiedName!~FileManagers"`
- The `FileManagers` tests need `filemanagers-settings.json` with real credentials: do not run them without asking.
- NUnit 5: `Assert.ThrowsAsync` returns a `Task` and must be awaited, otherwise the test asserts nothing.
- Installer: `Setup\build.bat`, then `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe Setup\Mpbkpu.iss`.

## Rules
- JSON serialization uses **Newtonsoft**: to exclude a field use `Newtonsoft.Json.JsonIgnore`, never the
  `System.Text.Json` one. Passwords must never end up in logs, databases or `settings.json`.
- GUI: no compiled bindings and no generated name fields; use `FindControl` and reflection bindings.
- GUI: every new text goes in **both** `it-IT.json` and `en-US.json`; icon-only buttons have `ToolTip.Tip` and
  `AutomationProperties.Name`.
- GUI: use the styles and resources of `App.axaml` (classes `card`, `chip`, `h1`, `h2`, `muted`, `mono`, `hint`,
  `accent`, `danger`, `dangerHover`, `icon`, `nav`, `log`) instead of local colors.
- CRLF line endings (`.gitattributes`).
- Always write comments and documentation in English.
- Do not duplicate code. Reuse common functions and use OOP patterns.
- File managers: uploads from APIs that read a stream go through `Utility/UploadStream`; full paths returned by
  `GetDirectoryContent` must keep the form of the requested path (jobs compute relative paths from them).
- Editing from Git Bash: `sed -i` turns CRLF into LF and heredocs mangle backslashes; prefer the Edit tool or Python
  scripts written to a file, then restore CRLF.

## GUI verification
Computer use cannot control the development exe. What works: start `MpbkpuGUI\bin\Debug\net10.0\mpbkpugui.exe`,
capture the window with `PrintWindow` (flag 2) and click the buttons with UI Automation (`InvokePattern` by name or
AutomationId, `TogglePattern` for Expanders, `ScrollPattern` to scroll). Settings live in
`%LOCALAPPDATA%\MpbkpuGUI\settings.json` and the secrets in `%LOCALAPPDATA%\MpbkpuGUI\secrets`: save a copy of
both before using test data and restore them afterwards. In UI Automation look for buttons by `ControlType` too
(text blocks share the names); Windows PowerShell 5.1 reads `.ps1` files without BOM as ANSI, so avoid non-ASCII
literals (e.g. "Sì") in scripts.
The test settings replace the ones the user's own GUI is using: ask the user to close the GUI first. Do the copy,
restore and cleanup of files outside the repository with a script run from the same terminal that starts the GUI
(agent shell tools may see a sandboxed copy of those files). A hand-written `settings.json` needs `$type` as the first
property of every object (Newtonsoft; use `[ordered]@{}` with `ConvertTo-Json`, otherwise the GUI exits at start); "Run"
opens a confirmation dialog ("Yes" button).

## CLI verification
Run the CLI in a real console (a terminal tab): with redirected output it crashes on `Console.CursorVisible` (TODO.md),
and the exit code is always 0, so check the results on disk or in the `--log` file (ignored by list,
list content and verify).

# TODO

## Now
- [ ] Commit the build warning fixes (`BackupJob.cs` CS8604, `PasswordDialog.axaml.cs` AVLN3001).
- [ ] Raise `MaxParallelTransfers` for OneDrive, Dropbox, Nextcloud once verified with real accounts (local file
  systems done: no limit).
- [ ] Try MEGA, Nextcloud, OneDrive, Dropbox, FTP, SFTP and SMB with real accounts (backup, sync, history, restore),
  also as additional destinations.
  OneDrive: check that `LargeFileUploadTask` reads the upload stream in order (`UploadStream` rejects backward seeks:
  a retried slice fails the upload instead of corrupting it).
- [ ] Check by hand: Windows animations (minimize, maximize, restore), warnings box in the history, windows narrower
  than 1180 px.

## Mockup features not implemented
- [ ] Restore from the GUI (today only from the CLI).
- [ ] Job list filters ("With warnings", "Never run").
- [ ] Phase list (stepper) on the running page.
- [ ] Service picker with chips in the settings (today a ComboBox).
- [ ] Message dialogs with a title and a detail line (today one message string).

## Fixes and improvements
- [ ] Multiple destinations: detect a destination holding the database of another backup set (it is read if newer;
  there is no backup set identifier).
- [ ] Warn before a backup/sync when the destination has less space than the bytes to copy (`GetSpace`).
- [ ] `BackupJob` ignores `Exclude` (CLI `--exclude`): reuse `ReadPathContent(..., exclude)` as the sync job does.
- [ ] CLI: `RestoreAction` and `SyncAction` ignore the job result, so failures are not reported; the exit code is
  always 0.
- [ ] CLI: crashes with "The handle is invalid" when the output is redirected (scripts, scheduled tasks, pipes):
  `Console.CursorVisible`, `CursorLeft` and `WindowWidth` are used without checking `Console.IsOutputRedirected`.
- [ ] CLI: `List`, `ListContent` and `Verify` ignore `--log` (their actions do not pass `LogFile`).
- [ ] `FileManagers` tests: make them explicit/skippable without credentials (today they block the suite).
- [ ] FTP: self-signed certificates are rejected; plain FTP sends the password in clear when the server has no TLS.
  Consider options to trust a certificate fingerprint and to require TLS.
- [ ] SFTP: no way to reset `%LOCALAPPDATA%\Mpbkpu\known_hosts` after a host key change; no passphrase for keys.
- [ ] MEGA: no 2FA; uploads have no modification date (sync with `SkipHashCheck` compares only the size).
- [ ] MediaFire: no modification date in the API; session token never renewed (`UserRenewSessionToken`);
  `IsCaseSensitive` probably wrong.
- [ ] SMB: a new connection per operation (EzSmb design, slow); dates not verified as UTC; no free space; hidden
  attributes not read.
- [ ] Local file systems cannot log: skipped links are not reported in the job log.
- [ ] `IsCaseSensitive` is never used; `LocalFileSystemLinux` is also used on macOS (case-insensitive by default).
- [ ] CLI: the encryption password cannot be remembered (GUI only).

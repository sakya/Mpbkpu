# TODO

## Now
- [ ] Check the Windows animations by hand (minimize, maximize, restore).
- [ ] Commit the secret store change (see PROJECT_STATUS.md).
- [ ] Check on screen: warnings box in the history, password dialog, narrow windows (<1180 px).

## Mockup features not implemented
- [ ] Restore from the GUI (today only from the CLI).
- [ ] Job list filters ("With warnings", "Never run").
- [ ] Phase list (stepper) on the running page.
- [ ] Service picker with chips in the settings (today a ComboBox).

## Fixes
- [ ] Try the MEGA, MediaFire, Nextcloud, OneDrive, Dropbox, FTP, SFTP and SMB fixes with real accounts (not covered by the offline
  tests). OneDrive: check that `LargeFileUploadTask` reads the upload stream sequentially (`UploadStream` rejects
  backward seeks, so a slice retry fails the upload instead of corrupting it).
- [ ] FTP: servers with self-signed certificates are now rejected; plain FTP still sends the password in clear when
  the server has no TLS. Consider an option to trust a certificate fingerprint and one to require TLS.
- [ ] SFTP: a changed host key is rejected: there is no GUI/CLI way to reset `%LOCALAPPDATA%\Mpbkpu\known_hosts`;
  private keys with a passphrase are not supported.
- [ ] Backups made before the UTC change restore modification dates shifted by the UTC offset (the saved local
  time is now read as UTC). Change detection is not affected (`Files.SameModificationTime`).
- [ ] Local file systems cannot log: skipped links are not reported in the job log.
- [ ] SMB: every operation opens a new SMB connection (EzSmb design): slow with many files. Dates come from
  SMBLibrary as they are (UTC not verified). Hidden attributes are not read.
- [ ] `IsCaseSensitive` is never used; `LocalFileSystemLinux` is also used on macOS (case-insensitive by default).
- [ ] MediaFire: no modification date in the API models (sync with `SkipHashCheck` compares only the size); session
  token never renewed (`UserRenewSessionToken`); `IsCaseSensitive` is true but MediaFire is probably case-insensitive.
- [ ] MEGA: no 2FA (`Login(user, password, mfa)`), uploads have no modification date (sync with `SkipHashCheck`
  compares only the size).
- [ ] `BackupJob` ignores `Exclude` (CLI `--exclude`): reuse `ReadPathContent(..., exclude)` as the sync job does.
- [ ] CLI: `SyncAction` ignores the job result, like `RestoreAction`.
- [ ] CLI: `RestoreAction` ignores the restore `Success`, so a failed restore is not reported.
- [ ] `FileManagers` tests: make them explicit/skippable without credentials (today they block the suite).

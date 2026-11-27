# TODO

## Now
- [ ] Raise `MaxParallelTransfers` for OneDrive, Dropbox, Nextcloud once verified with real accounts (local file
  systems done: no limit).
- [ ] Try MEGA, Nextcloud, OneDrive, Dropbox, FTP, SFTP and SMB with real accounts (backup, sync, history, restore),
  also as additional destinations.
  OneDrive: check that `LargeFileUploadTask` reads the upload stream in order (`UploadStream` rejects backward seeks:
  a retried slice fails the upload instead of corrupting it).
- [ ] Check by hand: Windows animations (minimize, maximize, restore), warnings box in the history, windows narrower
  than 1180 px.

## Mockup features not implemented
- [x] Restore of a whole backup from the GUI (history, into a chosen folder, deleting nothing).
- [x] Download and restore from the GUI: progress bar, files, bytes, time left and cancel button.
- [x] "Test connection" for every source and destination in the job settings.
- [ ] Job list filters ("With warnings", "Never run").
- [ ] Phase list (stepper) on the running page.
- [ ] Message dialogs with a title and a detail line (today one message string).

## Fixes and improvements
- [ ] CmdLineArgsParser (own package, github.com/sakya/CmdLineArgsParser) drops repeated values of a list option
  (`-e a -e a`, `--destinationUser bob --destinationUser bob`); the CLI works around it with `N=value`.
- [ ] CLI: `--exclude` is accepted by restore but `RestoreJob` has no `Exclude`.
- [ ] Restore: no free space check before writing (backup and sync have one).
- [ ] Compression to Dropbox and Nextcloud (uploads of unknown size): try a compressed backup and restore with real
  accounts; Nextcloud behind some proxies may refuse chunked transfer encoding.
- [ ] pCloud: try with a real account and a registered app (backup, sync, history, restore, verify); check that
  `uploadfile` honours `mtime`, the behaviour of names that differ only in case (`IsCaseSensitive`), and the EU server.
  For the explicit `FileManagers` tests add to `filemanagers-settings.json` an entry with `"Path":
  "pcloud://account/MpbkpuTest"` and `OAuth2Settings` (`AppId`, `AppSecret`, `RedirectUrl`).
- [ ] Try with real accounts the service options added on 2026-11-27 (FTP `?tls=required` and `?fingerprint=`,
  SFTP key passphrase and `?hostkey=`, MEGA modification date on upload and 2FA code, MediaFire session renewal).
- [ ] MediaFire: no modification date in the API (cannot be fixed); `IsCaseSensitive` probably wrong (unused, not
  verifiable without an account).
- [ ] SMB (EzSmb limits): a new connection per operation (slow), no free space, hidden attributes not read; would
  need another SMB library.
- [ ] Local file systems cannot log: skipped links are not reported in the job log.
- [ ] `IsCaseSensitive` is never used; `LocalFileSystemLinux` is also used on macOS (case-insensitive by default).
- [ ] CLI: the encryption password cannot be remembered (GUI only).

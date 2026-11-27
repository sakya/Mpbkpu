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
- [ ] Restore of a whole backup from the GUI (today the CLI; the backup browser downloads single files and
  directories). The download has no cancel button and no progress page.
- [ ] Job list filters ("With warnings", "Never run").
- [ ] Phase list (stepper) on the running page.
- [ ] Message dialogs with a title and a detail line (today one message string).

## Fixes and improvements
- [ ] CmdLineArgsParser (own package, github.com/sakya/CmdLineArgsParser) drops repeated values of a list option
  (`-e a -e a`, `--destinationUser bob --destinationUser bob`); the CLI works around it with `N=value`.
- [ ] CLI: `--exclude` is accepted by restore but `RestoreJob` has no `Exclude`.
- [ ] Restore: no free space check before writing (backup and sync have one).
- [ ] Try with real accounts the service options added on 2026-11-27 (FTP `?tls=required` and `?fingerprint=`,
  SFTP key passphrase and `?hostkey=`, MEGA modification date on upload, MediaFire session renewal).
- [ ] MEGA: no 2FA (the login would need a code every time: decide between asking it and storing the TOTP secret).
- [ ] MediaFire: no modification date in the API (cannot be fixed); `IsCaseSensitive` probably wrong (unused, not
  verifiable without an account).
- [ ] SMB (EzSmb limits): a new connection per operation (slow), no free space, hidden attributes not read; would
  need another SMB library.
- [ ] Local file systems cannot log: skipped links are not reported in the job log.
- [ ] `IsCaseSensitive` is never used; `LocalFileSystemLinux` is also used on macOS (case-insensitive by default).
- [ ] CLI: the encryption password cannot be remembered (GUI only).

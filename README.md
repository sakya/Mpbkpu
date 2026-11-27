<p align="center"><img src="icon.png" alt="Preview" width="150"></p>

# Multi Platform Backup Utility

Mpbkpu backs up your files to a local disk, a network share or a cloud service, with optional encryption and
compression. It runs on Windows, Linux and macOS and comes with a desktop app and a command line tool.

## Features

- **Full, incremental and differential backups.** The first backup copies everything; the next ones copy only what
  changed.
- **Automatic cleanup.** Start a new full backup every N days and keep only the most recent backups.
- **Encryption.** Protect a backup with a password (AES-256). Without the password nobody can read your files, not even
  you: keep it in a safe place.
- **Compression** to save space (on the destinations that support it).
- **More destinations for one backup.** When a single disk or account is too small, a backup can be split among
  several of them.
- **Folder synchronization.** Keep a copy of a folder identical to the original.
- **Verify, browse and restore.** Check that a backup is intact, look inside it, and get back single files and folders
  (from the app) or the whole backup (from the command line).
- **Credentials kept safe.** Passwords are stored in the secure storage of your operating system, never in plain text.

## Where you can back up

| Local | Network | Cloud |
|---|---|---|
| Folders and external disks | FTP / FTPS | Dropbox |
| | SFTP | MediaFire |
| | Windows shares (SMB) | MEGA |
| | | Nextcloud |
| | | OneDrive |
| | | pCloud |

Compression is available on local folders, FTP, SFTP, Dropbox and Nextcloud.

## Installation

On Windows, download and run the installer (`Mpbkpu-<version>-setup.exe`). It installs both the desktop app and the
command line tool. When you uninstall it, you can choose whether to keep your jobs and saved credentials; your backups
are never deleted.

## The desktop app

1. Click **New backup** or **New synchronization**.
2. Pick the source folder and one or more destinations. For cloud services you sign in to your account in the browser.
3. Set the options: backup type, encryption, compression, files to exclude, how many backups to keep.
4. Click **Run**.

From the job list you can also:

- see the history of a job and the result of every run;
- browse a backup and download files or folders from it (right click → *Download*);
- verify or delete a backup;
- copy the equivalent command line, to run the job from a script or a scheduled task.

The app is available in English and Italian, with a light and a dark theme.

## The command line

Back up a folder to an external disk:

```powershell
mpbkpu backup -t Full -s C:\Documents -d E:\Backup
```

Then only the changes, encrypted and compressed (you are asked for the password):

```powershell
mpbkpu backup -t Incremental -s C:\Documents -d E:\Backup -i -c Optimal
```

List the backups and restore one of them:

```powershell
mpbkpu list -b E:\Backup
mpbkpu restore -b E:\Backup -g <backup id> -d C:\Restored
```

Main commands: `backup`, `restore`, `list`, `listContent`, `verify`, `delete`, `syncDirectory`. Run `mpbkpu` without
arguments to see every command and option.

Useful options:

| Option | Meaning |
|---|---|
| `-e`, `--exclude` | File or folder to leave out (can be repeated) |
| `--fullEvery` | Start a new full backup when the last one is older than this many days |
| `--keepChains` | How many full backups (with their incremental ones) to keep |
| `--log` | Write a log file |
| `-y`, `--yes` | Answer yes to every question, for unattended runs |

The command returns `0` when it succeeds, `1` on error and `2` when cancelled, so scripts and scheduled tasks can
check the result.

### Cloud and network destinations

Destinations other than local folders are written as addresses, for example:

```text
ftp://user@server/backup
sftp://user@server/backup
smb://server/share/backup
dropbox://you@mail.com/Backup
onedrive://you@mail.com/Backup
mega://you@mail.com/Backup
```

The easiest way to get the right command for a cloud service is to set up the job in the desktop app and use
**Copy command**.

## License

GNU General Public License v3.0. See [LICENSE](LICENSE).

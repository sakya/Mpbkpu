# Backup structure
A backup is composed by
- A database file containing information about the backups
- Folders named with a GUID each containing 10000 files

In the database are saved the original file information (name, full path, size, creation date, ...)
The first backup is always a Full backup, subsequent backups can be Incremental or Differential
# Backup structure
A backup is composed by
- A database file (mpbkpu.db) containing information about the backups
- One directory named with a GUID for each backup
- The backup directory contains directories named with a GUID each containing 1000 files

In the database are saved the original file information (name, full path, size, creation date, ...)

The first backup is always a Full backup, subsequent backups can be Incremental or Differential
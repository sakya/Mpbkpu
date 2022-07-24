using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib
{
    public class BackupJob
    {
        #region private members

        private const int MaxFilesPerDirectory = 1000;
        private int? _bakupId;
        private StreamWriter? _logWriter = null;
        private CancellationToken? _cancellationToken = null;
        #endregion

        #region delegates
        public delegate void LogHandler(object sender, BackupJobSettings.LogLevels level, string message);
        public event LogHandler? LogEmitted;


        public class ConfirmArgs : EventArgs
        {
            public ConfirmArgs()
            {
            }

            public int FilesToBackup { get; set; }
            public long BytesToBackup { get; set; }
        }
        public delegate Task<bool> ConfirmHandler(object sender, ConfirmArgs e);
        public event ConfirmHandler? ConfirmBackup;
        #endregion

        public BackupJob(BackupJobSettings? settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            Settings = settings;
        }

        #region properties
        public BackupJobSettings Settings { get; private set; }
        #endregion

        #region public operations
        public async Task<bool> Run(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            // Open the log file
            if (!string.IsNullOrEmpty(Settings.LogFile)) {
                _logWriter = new StreamWriter(Settings.LogFile, false, System.Text.Encoding.UTF8);
            }

            ValidateSettings();

            LogInfo("Starting with settings:");
            LogInfo(JsonConvert.SerializeObject(Settings, Formatting.Indented));
            
            var dbFilePath = await CheckDestination();
            LogDebug($"Using db file: {dbFilePath}");
            var builder = new DbContextOptionsBuilder();
            var sqliteBuilder = builder.UseSqlite($"Data Source={dbFilePath}");
            using (var backupFile = new BackupFile()) {
                await backupFile.Open(dbFilePath);
                await backupFile.EmptyFiles();
                
                var lastBackup = await backupFile.LastBackup();
                var bkp = new DataModel.Backup()
                {
                    Name = Settings.Name,
                    Type = Settings.BackupType,
                    Settings = JsonConvert.SerializeObject(Settings, Formatting.Indented),
                    StartedAt = DateTime.UtcNow,
                };
                backupFile.Backups.Add(bkp);
                await backupFile.SaveChangesAsync();
                _bakupId = bkp.Id;

                // Read source
                LogInfo($"Reading source {Settings.Source}");
                if (!ReadPathContent(backupFile, Settings.Source, Settings.Source).Result) {
                    LogError("Failed to read source");
                    await SetBackupResult(backupFile, DataModel.Backup.Results.Error);
                    return false;
                }
                if (_cancellationToken?.IsCancellationRequested == true) {
                    LogInfo("Cancelled by user");
                    await SetBackupResult(backupFile, DataModel.Backup.Results.Cancelled);
                    return false;
                }
                await backupFile.SaveChangesAsync();

                // Compute files to backup
                // Read last backup items
                if (lastBackup != null) {
                    var lastItems = backupFile.BackupItems.AsNoTracking()
                        .Where(i => i.Volume.BackupId == lastBackup.Id);

                    foreach (var item in lastItems) {
                        // Check if the source file has changed
                        var sourceFile = await backupFile.Files.Where(f => f.FullPath == item.OriginalPath)
                            .FirstOrDefaultAsync();
                        if (sourceFile != null) {
                            if (sourceFile.Size != item.OriginalSize || sourceFile.LastModified != item.OriginalLastModified) {
                                sourceFile.Action = File.Actions.Backup;
                            } else {
                                sourceFile.Action = File.Actions.None;
                            }
                        }
                    }

                    await backupFile.SaveChangesAsync();
                }

                // Confirm backup
                if (ConfirmBackup != null) {
                    var cArgs = new ConfirmArgs()
                    {
                        FilesToBackup = await backupFile.Files.AsNoTracking()
                                                        .Where(f => f.Action == DataModel.File.Actions.Backup)
                                                        .CountAsync(),
                        BytesToBackup = await backupFile.Files.AsNoTracking()
                                                        .Where(f => f.Action == DataModel.File.Actions.Backup)
                                                        .Select(f => f.Size)
                                                        .SumAsync(),
                    };
                    if (!await ConfirmBackup.Invoke(this, cArgs)) {
                        LogInfo("Cancelled by user");
                        await SetBackupResult(backupFile, DataModel.Backup.Results.Cancelled);
                        return false;
                    }
                }

                // Backup files
                var filesToBackup = backupFile.Files.AsNoTracking()
                                    .Where(f => f.Action == DataModel.File.Actions.Backup);
                foreach (var fileToBackup in filesToBackup) {
                    if (_cancellationToken?.IsCancellationRequested == true) {
                        LogInfo("Cancelled by user");
                        await SetBackupResult(backupFile, DataModel.Backup.Results.Cancelled);
                        return false;
                    }
                    await BackupFile(backupFile, fileToBackup);
                }

                await SetBackupResult(backupFile, DataModel.Backup.Results.Completed);
                await backupFile.SaveChangesAsync();
                await backupFile.EmptyFiles();
            }
            LogInfo("Completed");
            return true;
        }
        #endregion

        #region private operations
        private void ValidateSettings()
        {
            if (string.IsNullOrEmpty(Settings.Source))
                throw new ArgumentNullException(nameof(Settings.Source), "Source cannot be empty");

            if (string.IsNullOrEmpty(Settings.Destination))
                throw new ArgumentNullException(nameof(Settings.Destination), "Destination cannot be empty");

            if (Settings.Source == Settings.Destination)
                throw new ArgumentException("Source and Destination cannot be the same");
            
            if (Settings.Destination.StartsWith(Settings.Source))
                throw new ArgumentException("Destination cannot be a subdirectory of Source");
        }

        /// <summary>
        /// Check the destination directory.
        /// Search for an existing backup.
        /// </summary>
        /// <returns>The path of the database file to use for this backup</returns>
        private async Task<string> CheckDestination()
        {
            string? path;
            var fm = FileManagerFactory.GetFileManager(Settings.Destination, out path);
            var dbFilePath = fm.CombinePath(path, MpbkpuLib.BackupFile.DefaultDatabaseFileName);
            if (!await fm.DirectoryExists(Settings.Destination))
                return dbFilePath;

            if (await fm.FileExists(dbFilePath))
                return dbFilePath;

            // If the database doesn't exists this is a full backup
            Settings.BackupType = BackupJobSettings.BackupTypes.Full;
            return dbFilePath;
        }
        
        private async Task<bool> BackupFile(BackupFile? backupFile, DataModel.File? sourceFile)
        {
            if (backupFile == null)
                throw new ArgumentNullException(nameof(backupFile));            
            if (sourceFile == null)
                throw new ArgumentNullException(nameof(sourceFile));

            switch (sourceFile.Type) {
                case File.FileTyes.Directory:
                    backupFile.BackupItems.Add(new BackupItem()
                    {
                        Type = sourceFile.Type,
                        OriginalPath = sourceFile.FullPath,
                        OriginalSize = sourceFile.Size,
                        OriginalExtension = sourceFile.Extension,
                        OriginalLastModified = sourceFile.LastModified,
                        OriginalCreated = sourceFile.Created,
                        OriginalHidden = sourceFile.Hidden,
                        OriginalName = sourceFile.Name
                    });
                    break;
                case File.FileTyes.File:
                    var backupItem = await GetNextBackupItem(backupFile);
                    backupItem.Type = sourceFile.Type;
                    backupItem.OriginalCreated = sourceFile.Created;
                    backupItem.OriginalExtension = sourceFile.Extension;
                    backupItem.OriginalHidden = sourceFile.Hidden;
                    backupItem.OriginalLastModified = sourceFile.LastModified;
                    backupItem.OriginalName = sourceFile.Name;
                    backupItem.OriginalPath = sourceFile.FullPath;
                    backupItem.OriginalSize = sourceFile.Size;
                    backupFile.BackupItems.Add(backupItem);
                    
                    string? destPath;
                    var sourceFm = FileManagers.FileManagerFactory.GetFileManager(sourceFile.FullPath, out _);
                    var destFm = FileManagers.FileManagerFactory.GetFileManager(Settings.Destination, out destPath);

                    var destFilePath = destFm.CombinePath(destPath, backupItem.Volume.Name, backupItem.Name);
                    LogDebug($"Backing up {sourceFile.FullPath} to {destFilePath}");
                    using (var sourceStream = await sourceFm.OpenFile(sourceFile.FullPath)) {
                        using (var destStream = await destFm.OpenFile(destFilePath, true)) {
                            long copied = 0;
                            byte[] buffer = new byte[4096];
                            while (copied < sourceFile.Size) {
                                int read = await sourceStream.ReadAsync(buffer, 0, buffer.Length);
                                await destStream.WriteAsync(buffer, 0, read);
                                copied += read;
                            }
                        }
                    }
                    break;
            }
            return true;
        }

        private async Task<DataModel.BackupItem> GetNextBackupItem(BackupFile backupFile)
        {
            var volume = await backupFile.BackupVolumes.AsNoTracking()
                .Where(v => v.Items.Count() < MaxFilesPerDirectory)
                .FirstOrDefaultAsync();
            if (volume == null) {
                volume = new BackupVolume()
                {
                    BackupId = _bakupId.Value,
                    Name = Guid.NewGuid().ToString("N")
                };
                backupFile.BackupVolumes.Add(volume);
            }
            string? destPath; 
            var destFm = FileManagers.FileManagerFactory.GetFileManager(Settings.Destination, out destPath);

            var res = new DataModel.BackupItem()
            {
                Name = Guid.NewGuid().ToString("N"),
                Volume = volume
            };
            var fullPath = destFm.CombinePath(destPath, volume.Name);
            if (!await destFm.DirectoryExists(fullPath))
                await destFm.CreateDirectory(fullPath);
            return res;
        }
        
        private async Task<bool> ReadPathContent(BackupFile? bkpFile, string? rootPath, string? path)
        {
            if (bkpFile == null)
                throw new ArgumentNullException(nameof(bkpFile));
            if (string.IsNullOrEmpty(rootPath))
                throw new ArgumentNullException(nameof(rootPath));
            if (string.IsNullOrEmpty(path))
                throw new ArgumentNullException(nameof(path));

            if (_cancellationToken?.IsCancellationRequested == true)
                return true;

            LogDebug($"  {path}");
            string? newPath;
            var fm = FileManagers.FileManagerFactory.GetFileManager(path, out newPath);
            if (fm == null)
                throw new Exception($"Cannot create FileManager for path {path}");

            await foreach (var file in fm.GetDirectoryContent(path, Settings.IncludeHiddenFiles)) {
                if (_cancellationToken?.IsCancellationRequested == true)
                    return true;

                file.Action = File.Actions.Backup;
                file.RootPath = rootPath;
                file.RelativePath = file.FullPath?.Substring(file.RootPath.Length);
                bkpFile.Files.Add(file);
                if (file.Type == DataModel.File.FileTyes.Directory)
                    await ReadPathContent(bkpFile, rootPath, file.FullPath);
            }
            return true;
        }

        private async Task<bool> SetBackupResult(BackupFile bkpFile, DataModel.Backup.Results result)
        {
            var bkp = await bkpFile.Backups.Where(b => b.Id == _bakupId).FirstOrDefaultAsync();
            if (bkp != null) {
                bkp.Result = result;
                bkp.CompletedAt = DateTime.UtcNow;

                await bkpFile.SaveChangesAsync();
                return true;
            }
            return false;
        }

        private void LogDebug(string message)
        {
            Log(BackupJobSettings.LogLevels.Debug, message);
        }

        private void LogInfo(string message)
        {
            Log(BackupJobSettings.LogLevels.Info, message);
        }

        private void LogWarning(string message)
        {
            Log(BackupJobSettings.LogLevels.Warning, message);
        }

        private void LogError(string message)
        {
            Log(BackupJobSettings.LogLevels.Warning, message);
        }

        private void Log(BackupJobSettings.LogLevels level, string message)
        {
            if (level < Settings.LogLevel)
                return;

            var levelStr = string.Empty;
            var color = ConsoleColor.Gray;

            switch (level) {
                case BackupJobSettings.LogLevels.Debug:
                    levelStr = "DBG";
                    color = ConsoleColor.DarkGray;
                    break;
                case BackupJobSettings.LogLevels.Info:
                    levelStr = "INF";
                    color = ConsoleColor.White;
                    break;
                case BackupJobSettings.LogLevels.Warning:
                    levelStr = "WRN";
                    color = ConsoleColor.DarkYellow;
                    break;
                case BackupJobSettings.LogLevels.Error:
                    levelStr = "ERR";
                    color = ConsoleColor.Red;
                    break;
            }
            var logLine = $"[{DateTime.Now.ToString("yyyyMMdd HH:mm:ss.fff")}][{levelStr}] {message}";

            if (_logWriter != null) {
                _logWriter.WriteLine(logLine);
                _logWriter.Flush();
            }

            if (Settings.LogToConsole) {
                Console.ForegroundColor = color;
                Console.WriteLine(logLine);
                Console.ResetColor();
            }

            LogEmitted?.Invoke(this, level, logLine);
        }
        #endregion
    }
}
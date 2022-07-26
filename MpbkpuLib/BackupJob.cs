using System.Security.Cryptography;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using MpbkpuLib.DataModel;
using MpbkpuLib.FileManagers;
using File = MpbkpuLib.DataModel.File;

namespace MpbkpuLib
{
    /// <summary>
    /// A job to backup files
    /// </summary>
    public class BackupJob
    {
        #region classes

        public class LastUsedVolume
        {
            public int? Id { get; set; }
            public string? Name { get; set; }
            public int? ItemsCount { get; set; }
        }
        #endregion
        #region private members

        private Backup? _backup;
        private LastUsedVolume? _lastUsedVolume;
        private StreamWriter? _logWriter;
        private CancellationToken? _cancellationToken;
        #endregion

        #region delegates
        public delegate void LogHandler(object sender, BackupJobSettings.LogLevels level, string message);
        public event LogHandler? LogEmitted;
        
        public class ConfirmArgs : EventArgs
        {
            public int FilesToBackup { get; set; }
            public long BytesToBackup { get; set; }
        }
        public delegate Task<bool> ConfirmHandler(object sender, ConfirmArgs e);
        public event ConfirmHandler? ConfirmBackup;
        
        public delegate void ProgressHandler(object sender, int file, int totalFiles, long bytes, long totalBytes);

        public event ProgressHandler? Progress;
        
        public delegate void FileProgressHandler(object sender, long bytes, long total);

        public event FileProgressHandler? FileProgress;        
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
        /// <summary>
        /// Run the backp using the given <see cref="Settings"/>
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/></param>
        /// <returns>True on success</returns>
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
            using (var backupFile = new BackupFile()) {
                await backupFile.Open(dbFilePath);
                await backupFile.EmptyFiles();
                var transaction = backupFile.BeginTransaction();

                try {
                    await RunPrimitive(backupFile);
                    await transaction.CommitAsync();
                } catch (Exception ex) {
                    await transaction.RollbackAsync();
                    LogError(ex);
                    throw;
                } 
            }

            return true;
        }
        #endregion

        #region private operations
        /// <summary>
        /// Run the backp using the given <see cref="Settings"/>
        /// </summary>
        /// <param name="backupFile">The <see cref="BackupFile"/> to use</param>
        /// <returns>True on success</returns>
        public async Task<bool> RunPrimitive(BackupFile backupFile)
        {
            _lastUsedVolume = null;
            Backup? lastBackup = null;
            if (Settings.BackupType == BackupJobSettings.BackupTypes.Incremental) {
                // Incremental starts always from the last full backup
                lastBackup = await backupFile.LastBackup(BackupJobSettings.BackupTypes.Full); 
            }
            // If there is no previous backup this is a full backup
            if (lastBackup == null && Settings.BackupType != BackupJobSettings.BackupTypes.Full) {
                LogWarning("No previous backup found, switching to Full backup");
                Settings.BackupType = BackupJobSettings.BackupTypes.Full;
            }

            _backup = new Backup()
            {
                Name = Settings.Name,
                Type = Settings.BackupType,
                Settings = JsonConvert.SerializeObject(Settings, Formatting.Indented),
                StartedAt = DateTime.UtcNow,
            };

            // Create the backup folder
            string destPath;
            var destFm = FileManagerFactory.GetFileManager(Settings.Destination, out destPath);
            var backupDirectory = destFm.CombinePath(destPath, _backup.Guid);
            if (await destFm.DirectoryExists(backupDirectory))
                throw new Exception($"The backup destination {backupDirectory} exist");
            await destFm.CreateDirectory(backupDirectory);

            backupFile.Backups.Add(_backup);
            await backupFile.SaveChangesAsync();
            backupFile.Detach(_backup);

            // Read source
            LogInfo($"Reading source {Settings.Source}");
            if (!ReadPathContent(backupFile, Settings.Source, Settings.Source).Result) {
                LogError("Failed to read source");
                await SetBackupResult(backupFile, Backup.Results.Error);
                return false;
            }
            if (_cancellationToken?.IsCancellationRequested == true) {
                LogInfo("Cancelled by user");
                await SetBackupResult(backupFile, Backup.Results.Cancelled);
                return false;
            }
            await backupFile.SaveChangesAsync();

            // Compute files to backup (only if not a full backup)
            // Read last backup items
            if (lastBackup != null && _backup.Type != BackupJobSettings.BackupTypes.Full) {
                var lastItems = backupFile.BackupItems.AsNoTracking()
                    .Where(i => i.BackupId == lastBackup.Id);

                foreach (var item in lastItems) {
                    // Check if the source file has changed
                    var sourceFile = await backupFile.Files.Where(f => f.FullPath == item.OriginalPath)
                        .FirstOrDefaultAsync();
                    if (sourceFile != null) {
                        if (sourceFile.Size != item.OriginalSize || sourceFile.LastModified != item.OriginalLastModified) {
                            sourceFile.Action = File.Actions.Backup;
                            sourceFile.Modified = true;
                        } else {
                            sourceFile.Action = File.Actions.None;
                        }
                    } else {
                        backupFile.Files.Add(new File()
                        {
                            Action = File.Actions.Delete,
                            Created = item.OriginalCreated,
                            Extension = item.OriginalExtension,
                            FullPath = item.OriginalPath,
                            Hidden = item.OriginalHidden,
                            Name = item.OriginalName,
                            Size = item.OriginalSize,
                            Type = item.Type
                        });
                        await backupFile.SaveChangesAsync();
                    }
                }

                await backupFile.SaveChangesAsync();
            }

            // Confirm backup
            var cArgs = new ConfirmArgs()
            {
                FilesToBackup = await backupFile.Files.AsNoTracking()
                    .Where(f => f.Action == File.Actions.Backup)
                    .CountAsync(),
                BytesToBackup = await backupFile.Files.AsNoTracking()
                    .Where(f => f.Action == File.Actions.Backup)
                    .Select(f => f.Size)
                    .SumAsync(),
            };            
            if (ConfirmBackup != null) {
                if (!await ConfirmBackup.Invoke(this, cArgs)) {
                    LogInfo("Cancelled by user");
                    await SetBackupResult(backupFile, Backup.Results.Cancelled);
                    return false;
                }
            }

            // Backup files
            Utility.EncryptDecrypt? encryptor = null;
            if (!string.IsNullOrEmpty(Settings.EncryptPassword)) {
                encryptor = new Utility.EncryptDecrypt(Settings.EncryptPassword);
            }

            int fileIdx = 0;
            long bytes = 0;
            var filesToBackup = backupFile.Files.AsNoTracking()
                                .Where(f => f.Action == File.Actions.Backup || f.Action == File.Actions.Delete);
            foreach (var fileToBackup in filesToBackup) {
                if (_cancellationToken?.IsCancellationRequested == true) {
                    LogInfo("Cancelled by user");
                    await SetBackupResult(backupFile, Backup.Results.Cancelled);
                    return false;
                }

                if (fileToBackup.Action == File.Actions.Backup) {
                    await BackupItem(backupFile, fileToBackup, encryptor);

                    bytes += fileToBackup.Size;
                    Progress?.Invoke(this, ++fileIdx, cArgs.FilesToBackup, bytes, cArgs.BytesToBackup);
                } else {
                    await DeleteItem(backupFile, fileToBackup);
                }
            }

            await SetBackupResult(backupFile, Backup.Results.Completed);
            await backupFile.SaveChangesAsync();
            await backupFile.EmptyFiles();
            LogInfo("Completed");
            return true;
        }
        
        /// <summary>
        /// Validate the <see cref="BackupJobSettings"/>
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
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

            if (Settings.MaxFilesPerDirectory <= 0)
                throw new ArgumentException("MaxFilesPerDirectory must be greater than zero");
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
            var dbFilePath = fm.CombinePath(path, BackupFile.DefaultDatabaseFileName);
            if (!await fm.DirectoryExists(Settings.Destination)) {
                await fm.CreateDirectory(Settings.Destination);
                return dbFilePath;
            }

            if (await fm.FileExists(dbFilePath)) {
                LogDebug($"Found existing db file {dbFilePath}");
                return dbFilePath;
            }

            return dbFilePath;
        }
        
        /// <summary>
        /// Backup a <see cref="DataModel.File"/>
        /// </summary>
        /// <param name="backupFile">The <see cref="BackupFile"/></param>
        /// <param name="sourceFile">The <see cref="DataModel.File"/> to backup</param>
        /// <param name="encryptor">The optional <see cref="Utility.EncryptDecrypt"/> to enxrypt the file</param>
        /// <returns>True on success</returns>
        /// <exception cref="ArgumentNullException"></exception>
        private async Task<bool> BackupItem(BackupFile? backupFile, File? sourceFile, Utility.EncryptDecrypt? encryptor)
        {
            if (backupFile == null)
                throw new ArgumentNullException(nameof(backupFile));            
            if (sourceFile == null)
                throw new ArgumentNullException(nameof(sourceFile));

            var backupItem = await GetNextBackupItem(backupFile);
            backupItem.BackupId = _backup.Id;
            backupItem.Action = sourceFile.Modified ? DataModel.BackupItem.Actions.Modified : DataModel.BackupItem.Actions.Added;
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
            var destFm = FileManagerFactory.GetFileManager(Settings.Destination, out destPath);
            var destFilePath = destFm.CombinePath(destPath, _backup.Guid, backupItem.Volume.Name, backupItem.Name);
            switch (sourceFile.Type) {
                case File.FileTyes.Directory:
                    // Create a zero byte file as a placeholder
                    using (var destStream = await destFm.OpenFile(destFilePath, true)) {
                    }
                    break;
                case File.FileTyes.File:
                    var sourceFm = FileManagerFactory.GetFileManager(sourceFile.FullPath, out _);
                    LogDebug($"Backing up {sourceFile.FullPath} to {destFilePath}");
                    using (var sourceStream = await sourceFm.OpenFile(sourceFile.FullPath)) {
                        using (var destStream = await destFm.OpenFile(destFilePath, true)) {
                            if (encryptor != null) {
                                // Encrypt file
                                var progressAction = new Action<long>((copied) =>
                                {
                                    FileProgress?.Invoke(this, copied, sourceFile.Size);
                                });
                                await encryptor.Encrypt(sourceStream, destStream, progressAction);
                            } else {
                                // Normal copy
                                long copied = 0;
                                byte[] buffer = new byte[4096];
                                while (copied < sourceFile.Size) {
                                    int read = await sourceStream.ReadAsync(buffer, 0, buffer.Length);
                                    await destStream.WriteAsync(buffer, 0, read);
                                    copied += read;
                                    FileProgress?.Invoke(this, copied, sourceFile.Size);
                                }
                            }
                        }
                    }
                    break;
            }
            await backupFile.SaveChangesAsync();
            return true;
        }

        private async Task<bool> DeleteItem(BackupFile? backupFile, File? sourceFile)
        {
            var item = new BackupItem()
            {
                BackupId = _backup.Id,
                Action = DataModel.BackupItem.Actions.Deleted,
                Type = sourceFile.Type,
                OriginalCreated = sourceFile.Created,
                OriginalExtension = sourceFile.Extension,
                OriginalHidden = sourceFile.Hidden,
                OriginalLastModified = sourceFile.LastModified,
                OriginalName = sourceFile.Name,
                OriginalPath = sourceFile.FullPath,
                OriginalSize = sourceFile.Size,

            };
            backupFile.BackupItems.Add(item);
            await backupFile.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Get the next <see cref="DataModel.BackupItem"/>.
        /// Find the first free <see cref="BackupVolume"/> or create a new one for the file
        /// </summary>
        /// <param name="backupFile">The <see cref="BackupFile"/> to backup</param>
        /// <returns>A <see cref="DataModel.BackupItem"/></returns>
        private async Task<BackupItem> GetNextBackupItem(BackupFile backupFile)
        {
            int? volumeId = null;
            string? volumeName = null; 
            if (_lastUsedVolume?.ItemsCount < Settings.MaxFilesPerDirectory) {
                volumeId = _lastUsedVolume.Id;
                volumeName = _lastUsedVolume.Name;
                _lastUsedVolume.ItemsCount++;
            } else {
                var volume = await backupFile.BackupVolumes.AsNoTracking()
                    .Where(v => v.BackupId == _backup.Id && v.Items.Count() < Settings.MaxFilesPerDirectory)
                    .FirstOrDefaultAsync();
                if (volume == null) {
                    volume = new BackupVolume()
                    {
                        BackupId = _backup.Id,
                        Name = Guid.NewGuid().ToString("N"),
                    };
                    backupFile.BackupVolumes.Add(volume);
                    await backupFile.SaveChangesAsync();
                }

                volumeId = volume.Id;
                volumeName = volume.Name;
                _lastUsedVolume = new LastUsedVolume()
                {
                    Id = volume.Id,
                    Name = volume.Name,
                    ItemsCount = volume.Items?.Count() ?? 1
                };
            }
            string? destPath; 
            var destFm = FileManagerFactory.GetFileManager(Settings.Destination, out destPath);

            var res = new BackupItem()
            {
                Name = Guid.NewGuid().ToString("N"),
                VolumeId = volumeId.Value
            };
            var volumePath = destFm.CombinePath(destPath, _backup.Guid, volumeName);
            if (!await destFm.DirectoryExists(volumePath))
                await destFm.CreateDirectory(volumePath);
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
            var fm = FileManagerFactory.GetFileManager(path, out newPath);
            if (fm == null)
                throw new Exception($"Cannot create FileManager for path {path}");

            await foreach (var file in fm.GetDirectoryContent(newPath, Settings.IncludeHiddenFiles)) {
                if (_cancellationToken?.IsCancellationRequested == true)
                    return true;

                file.Action = File.Actions.Backup;
                file.RootPath = rootPath;
                file.RelativePath = file.FullPath?.Substring(file.RootPath.Length);
                bkpFile.Files.Add(file);
                if (file.Type == File.FileTyes.Directory)
                    await ReadPathContent(bkpFile, rootPath, file.FullPath);
            }
            return true;
        }

        private async Task<bool> SetBackupResult(BackupFile bkpFile, Backup.Results result)
        {
            var bkp = await bkpFile.Backups.Where(b => b.Id == _backup.Id).FirstOrDefaultAsync();
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

        private void LogError(Exception exception)
        {
            var ex = exception;
            while (ex != null) {
                LogError(ex.Message);
                if (!string.IsNullOrEmpty(ex.StackTrace))
                    LogError(ex.StackTrace);
                ex = ex.InnerException;
            }
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
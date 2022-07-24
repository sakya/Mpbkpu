using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MpbkpuLib
{
    public class BackupJobSettings
    {
        /// <summary>
        /// The backup type
        /// https://phoenixnap.com/kb/full-vs-incremental-vs-differential-backup
        /// </summary>
        public enum BackupTypes
        {
            /// <summary>
            /// Backup all the files
            /// </summary>
            Full,
            /// <summary>
            /// Backup only changed files since the last full/incremental backup
            /// </summary>
            Incremental,
            /// <summary>
            /// Backup only changed files since the last full backup
            /// </summary>
            Differential
        }

        public enum LogLevels
        {
            Debug,
            Info,
            Warning,
            Error
        }

        /// <summary>
        /// The job name
        /// </summary>
        /// <value></value>
        public string? Name { get; set; }
        public BackupTypes BackupType { get; set; }
        /// <summary>
        /// The backup source
        /// </summary>
        /// <value></value>
        public string? Source { get; set; }
        /// <summary>
        /// The backup destination
        /// </summary>
        /// <value></value>
        public string? Destination { get; set; }

        public bool IncludeHiddenFiles { get; set; }
        
        /// <summary>
        /// The password used to encrypt/decrypt the files
        /// </summary>
        public string? EncryptPassword { get; set; }
        /// <summary>
        /// If set to true files are compressed during backup
        /// </summary>
        /// <value></value>
        public bool Compress { get; set; }

        public bool LogToConsole { get; set; }
        public string? LogFile { get; set; }
        public LogLevels LogLevel { get; set; }
    }
}

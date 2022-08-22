using System.ComponentModel.DataAnnotations;
using MpbkpuLib.Abstracts;
using MpbkpuLib.Jobs;

namespace MpbkpuLib.DataModel
{
    public class Backup : BaseModel
    {
        /// <summary>
        /// The backup type
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

        public enum Results
        {
            None = 0,
            /// <summary>
            /// Completed
            /// </summary>
            Completed,
            CompletedWithWarnings,
            /// <summary>
            /// Cancelled by user
            /// </summary>
            Cancelled,
            /// <summary>
            /// Error
            /// </summary>
            Error
        }

        public Backup()
        {
            Guid = System.Guid.NewGuid().ToString("N");
        }

        public string? Name { get; set; }
        public string Guid { get; set; }

        public BackupTypes Type { get; set; }

        [MaxLength(4096)]
        public string? Settings { get; set; }
        public Results Result { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public TimeSpan? Duration
        {
            get
            {
                if (StartedAt.HasValue && CompletedAt.HasValue)
                    return CompletedAt.Value - StartedAt.Value;
                return null;
            }
        }

        public ICollection<BackupWarning> Warnings { get; set; } = null!;
        public ICollection<BackupItem> Items { get; set; } = null!;
    }
}
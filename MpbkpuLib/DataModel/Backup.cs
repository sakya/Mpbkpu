using System.ComponentModel.DataAnnotations;
using MpbkpuLib.Abstracts;
using MpbkpuLib.Jobs;

namespace MpbkpuLib.DataModel
{
    public class Backup : BaseModel
    {
        public enum Results
        {
            None = 0,
            Completed,
            CompletedWithWarnings,
            Cancelled,
            Error
        }

        public Backup()
        {
            Guid = System.Guid.NewGuid().ToString("N");
        }

        public string? Name { get; set; }
        public string Guid { get; set; }

        public BackupJobSettings.BackupTypes Type { get; set; }

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
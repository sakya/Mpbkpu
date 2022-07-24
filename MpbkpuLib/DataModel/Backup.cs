using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public string? Name { get; set; }
        public BackupJobSettings.BackupTypes Type { get; set; }
        [MaxLength(4096)]
        public string? Settings { get; set; }
        public Results Result { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public ICollection<BackupWarning>? Warnings { get; set; }
        public ICollection<BackupVolume>? Volumes { get; set; }
    }
}
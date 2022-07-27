using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MpbkpuLib.Abstracts;

namespace MpbkpuLib.DataModel
{
    public class BackupWarning : BaseModel
    {
        [Required]
        public int BackupId { get; set; }
        public Backup? Backup { get; set; }

        [MaxLength(2048)]
        public string? Message { get; set; }
    }
}
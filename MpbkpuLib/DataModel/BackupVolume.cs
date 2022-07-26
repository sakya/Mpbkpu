using System.ComponentModel.DataAnnotations;

namespace MpbkpuLib.DataModel;

public class BackupVolume : BaseModel
{
    [Required]
    public int BackupId { get; set; }
    public Backup? Backup { get; set; }

    [Required]
    public string? Name { get; set; }
    
    public ICollection<BackupItem>? Items { get; set; }
}
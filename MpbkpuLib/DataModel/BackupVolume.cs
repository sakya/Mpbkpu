using System.ComponentModel.DataAnnotations;

namespace MpbkpuLib.DataModel;

public class BackupVolume : BaseModel
{
    [Required]
    public string? Name { get; set; }
    
    public ICollection<BackupItem>? Items { get; set; }
}
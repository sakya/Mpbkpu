using System.ComponentModel.DataAnnotations;

namespace MpbkpuLib.DataModel;

public class BackupItem : BaseModel
{
    [Required]
    public int BackupId { get; set; }
    public Backup? Backup { get; set; }
    
    public int VolumeId { get; set; }
    public BackupVolume? Volume { get; set; }
    
    public File.FileTyes Type { get; set; }
    
    [MaxLength(255)]
    public string? Name { get; set; }

    [MaxLength(255)]
    public string? OriginalPath { get; set; }    
    [MaxLength(255)]
    public string? OriginalName { get; set; }
    [MaxLength(255)]
    public string? OriginalExtension { get; set; }
    public long OriginalSize { get; set; }
    public bool OriginalHidden { get; set; }
    public DateTime? OriginalCreated { get; set; }
    public DateTime? OriginalLastModified { get; set; }    
}
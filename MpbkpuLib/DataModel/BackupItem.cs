using System.ComponentModel.DataAnnotations;
using MpbkpuLib.Abstracts;

namespace MpbkpuLib.DataModel;

public class BackupItem : BaseModel
{
    public enum Actions
    {
        Added,
        Modified,
        Deleted
    }

    [Required]
    public int BackupId { get; set; }
    public Backup? Backup { get; set; }

    public int VolumeId { get; set; }
    public BackupVolume? Volume { get; set; }

    public File.FileTyes Type { get; set; }

    public Actions Action { get; set; }

    [MaxLength(255)]
    public string? Name { get; set; }

    /// <summary>
    /// The MD5 hash of the backup file
    /// </summary>
    [MaxLength(255)]
    public string? MD5 { get; set; }
    /// <summary>
    /// The size of the backup file
    /// </summary>
    public long Size { get; set; }

    [MaxLength(255)]
    public string? OriginalPath { get; set; }
    [MaxLength(255)]
    public string? OriginalName { get; set; }
    [MaxLength(255)]
    public string? OriginalExtension { get; set; }
    /// <summary>
    /// The size of the original file
    /// </summary>
    public long OriginalSize { get; set; }
    public bool OriginalHidden { get; set; }
    public DateTime? OriginalCreated { get; set; }
    public DateTime? OriginalLastModified { get; set; }
}
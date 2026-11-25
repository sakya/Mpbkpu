using System.ComponentModel.DataAnnotations;
using MpbkpuLib.Abstracts;

namespace MpbkpuLib.DataModel;

public class BackupVolume : BaseModel
{
    [Required]
    public int BackupId { get; set; }
    public Backup? Backup { get; set; }

    [Required]
    public string? Name { get; set; }

    /// <summary>
    /// The destination holding the volume. Null for the volumes created before multiple destinations:
    /// they are stored in the destination the database was read from.
    /// </summary>
    public int? DestinationId { get; set; }
    public Destination? Destination { get; set; }

    public ICollection<BackupItem>? Items { get; set; }
}
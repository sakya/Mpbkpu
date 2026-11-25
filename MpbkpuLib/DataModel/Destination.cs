using System.ComponentModel.DataAnnotations;
using MpbkpuLib.Abstracts;

namespace MpbkpuLib.DataModel;

/// <summary>
/// A destination where the volumes of the backups are stored
/// </summary>
public class Destination : BaseModel
{
    /// <summary>
    /// The <see cref="Jobs.BackupDestination.Id"/> of the destination
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string? Key { get; set; }

    /// <summary>
    /// The path of the destination with the credentials masked, to tell the user which destination is missing
    /// </summary>
    [MaxLength(4096)]
    public string? Path { get; set; }

    public ICollection<BackupVolume>? Volumes { get; set; }
}

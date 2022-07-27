using System.ComponentModel.DataAnnotations;

namespace MpbkpuLib.Abstracts;

public abstract class BaseModel
{
    [Required]
    public int Id { get; set; }
}

using System.IO.Compression;
using System.Text.Json.Serialization;
using MpbkpuGUI.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Models;

public class BackupJob : Job
{
    public Backup.BackupTypes? Type { get; set; }
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.NoCompression;
    public bool Encrypted { get; set; }
    public int FullBackupEveryDays { get; set; }
    public int KeepChains { get; set; }

    [JsonIgnore]
    public string? Password { get; set; }
}
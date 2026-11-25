using System.IO.Compression;
using Newtonsoft.Json;
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

    // Never saved in the settings file
    [JsonIgnore]
    public string? Password { get; set; }

    [JsonIgnore]
    public bool IsCompressed => CompressionLevel != CompressionLevel.NoCompression;
}
using System.IO.Compression;
using MpbkpuGUI.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Models;

public class BackupJob : Job
{
    public Backup.BackupTypes? Type { get; set; }
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.NoCompression;
}
using MpbkpuLib.Abstracts;

namespace MpbkpuLib.Jobs;

public class ListContentJobResult : BaseJobResult
{
    public int FilesCount { get; set; }
    public int DirectoriesCount { get; set; }
}
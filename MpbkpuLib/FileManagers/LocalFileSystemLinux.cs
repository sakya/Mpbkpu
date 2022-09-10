using MpbkpuLib.Abstracts;

namespace MpbkpuLib.FileManagers;

public class LocalFileSystemLinux : BaseLocalFileSystem
{
    public override bool IsCaseSensitive => true;

    public override char PathSeparator => '/';

    protected override bool IsFileHidden(FileSystemInfo fileInfo)
    {
        return fileInfo.Name.StartsWith(".");
    }
}
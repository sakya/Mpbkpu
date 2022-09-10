using MpbkpuLib.Abstracts;

namespace MpbkpuLib.FileManagers;

public class LocalFileSystemWindows : BaseLocalFileSystem
{
    public override bool IsCaseSensitive => false;

    public override char PathSeparator => '\\';

    protected override bool IsFileHidden(FileSystemInfo fileInfo)
    {
        return fileInfo.Attributes.HasFlag(FileAttributes.Hidden);

    }
}
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Controls.Models;

public class PositionDetailModel : INotifyPropertyChanged
{
    public enum FileSystemTypes
    {
        FileSystem,
        DropBox,
        Ftp,
        MediaFire,
        Mega,
        Nextcloud,
        OneDrive,
        Sftp,
        Smb
    }

    private FileSystemTypes _type;
    public FileSystemTypes Type
    {
        get => _type;
        set
        {
            if (SetField(ref _type, value)) {
                OnPropertyChanged(nameof(IsLocalFileSystem));
                OnPropertyChanged(nameof(SupportUserPassword));
                OnPropertyChanged(nameof(SupportOAuth));
            }
        }
    }

    private Job.Position? _position;
    public Job.Position? Position
    {
        get => _position;
        set => SetField(ref _position, value);
    }

    public bool IsLocalFileSystem => Type == FileSystemTypes.FileSystem;

    public bool SupportUserPassword =>
        Type is FileSystemTypes.Ftp or FileSystemTypes.Mega or FileSystemTypes.Nextcloud or
            FileSystemTypes.Sftp or FileSystemTypes.Smb or FileSystemTypes.MediaFire;

    public bool SupportOAuth =>
        Type is FileSystemTypes.Nextcloud or FileSystemTypes.DropBox or FileSystemTypes.OneDrive;


    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
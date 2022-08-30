using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Models;

public class BackupOpenPageModel : INotifyPropertyChanged
{
    public class BackupData
    {
        public Backup? Backup { get; set; }
        public long Items { get; set; }
        public long Size { get; set; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private ObservableCollection<BackupData>? _backups;
    public ObservableCollection<BackupData>? Backups
    {
        get => _backups;
        set
        {
            SetField(ref _backups, value);
        }
    }

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
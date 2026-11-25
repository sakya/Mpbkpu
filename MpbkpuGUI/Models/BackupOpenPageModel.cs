using System.Collections.Generic;
using System.Collections.ObjectModel;
using MpbkpuGUI.Abstracts;
using MpbkpuLib.DataModel;

namespace MpbkpuGUI.Models;

public class BackupOpenPageModel : BaseModel
{
    public class BackupData
    {
        public Backup? Backup { get; set; }
        public long Items { get; set; }
        public long Size { get; set; }
        public string ItemsText => string.Format(Localizer.Localizer.Instance["FilesCount"], Items.ToString("N0"));

        /// <summary>
        /// Title of the chain starting with this backup (backups are listed newest first), null for the other backups
        /// </summary>
        public string? ChainTitle { get; set; }
        public string? ChainSummary { get; set; }

        public List<string> Warnings { get; set; } = new();
        public bool HasWarnings => Warnings.Count > 0;
    }

    private string? _spinnerMessage;
    public string? SpinnerMessage
    {
        get => _spinnerMessage;
        set => SetField(ref _spinnerMessage, value);
    }

    private ObservableCollection<BackupData>? _backups;
    public ObservableCollection<BackupData>? Backups
    {
        get => _backups;
        set => SetField(ref _backups, value);
    }
}
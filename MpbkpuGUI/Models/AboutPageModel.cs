using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Models;

public class AboutPageModel : BaseModel
{
    private string? _title;
    public string? Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    private string? _copyright;
    public string? Copyright
    {
        get => _copyright;
        set => SetField(ref _copyright, value);
    }

    private string? _description;
    public string? Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }
}
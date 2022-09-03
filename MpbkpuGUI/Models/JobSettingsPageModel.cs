using System.Collections.Generic;
using MpbkpuGUI.Abstracts;

namespace MpbkpuGUI.Models;

public class JobSettingsPageModel : BaseModel
{
    private Job? _job;
    public Job? Job
    {
        get => _job;
        set => SetField(ref _job, value);
    }

    private List<Controls.ErrorContainer.ErrorData>? _errors;
    public List<Controls.ErrorContainer.ErrorData>? Errors
    {
        get => _errors;
        set => SetField(ref _errors, value);
    }
}
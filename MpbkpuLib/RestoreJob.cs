using MpbkpuLib.Abstracts;

namespace MpbkpuLib;

/// <summary>
/// A job to restore files from a backup
/// </summary>
public class RestoreJob : BaseJob
{
    #region classes

    #endregion

    public RestoreJob(RestoreJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new RestoreJobSettings Settings
    {
        get { return (base.Settings as RestoreJobSettings)!; }
    }

    #endregion

    #region public operations
    public override Task<bool> Run(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
    #endregion

    #region private operations

    #endregion
}
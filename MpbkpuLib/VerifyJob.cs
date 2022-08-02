using MpbkpuLib.Abstracts;

namespace MpbkpuLib;

public class VerifyJob: BaseJob
{
    #region classes

    #endregion

    public VerifyJob(VerifyJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new VerifyJobSettings Settings
    {
        get { return (base.Settings as VerifyJobSettings)!; }
    }

    #endregion

    #region public operations
    public override Task<bool> Run(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
    #endregion

    #region private operations
    /// <summary>
    /// Validate the <see cref="VerifyJobSettings"/>
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    protected override void ValidateSettings()
    {
    }

    #endregion
}
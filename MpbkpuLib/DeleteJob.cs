using MpbkpuLib.Abstracts;

namespace MpbkpuLib;

public class DeleteJob : BaseJob
{
    public DeleteJob(DeleteJobSettings? settings) :
        base(settings)
    {
    }

    #region properties
    public new DeleteJobSettings Settings
    {
        get { return (base.Settings as DeleteJobSettings)!; }
    }
    #endregion

    #region public operations
    /// <summary>
    /// Run the job using the given <see cref="Settings"/>
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/></param>
    /// <returns>True on success</returns>
    public override async Task<bool> Run(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
    #endregion

    #region private operations

    #endregion
}
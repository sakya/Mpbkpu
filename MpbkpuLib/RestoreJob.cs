namespace MpbkpuLib;

/// <summary>
/// A job to restore files from a backup
/// </summary>
public class RestoreJob
{
    #region classes
    
    #endregion

    public RestoreJob(RestoreJobSettings? settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        Settings = settings;
    }
    
    #region properties
    public RestoreJobSettings Settings { get; private set; }
    #endregion    
    
    #region public operations
    public async Task<bool> Run(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
    #endregion
    
    #region private operations
    
    #endregion
}
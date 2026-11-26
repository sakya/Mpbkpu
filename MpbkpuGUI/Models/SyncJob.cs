using MpbkpuGUI.Abstracts;
using MpbkpuLib.Jobs;
using MpbkpuLib.Utility;

namespace MpbkpuGUI.Models;

public class SyncJob : Job
{
    /// <summary>
    /// The settings of the library job that runs this synchronization
    /// </summary>
    public SyncDirectoryJobSettings CreateSettings()
    {
        return new SyncDirectoryJobSettings()
        {
            Source = Source!.FullPath,
            SourceUser = Source.User,
            SourcePassword = Source.Password,
            SourceOauthSettings = Source.GetOAuth2Settings(),

            Destination = Destination!.FullPath,
            DestinationUser = Destination.User,
            DestinationPassword = Destination.Password,
            DestinationOauthSettings = Destination.GetOAuth2Settings(),

            SkipHashCheck = SkipHashCheck,
            RetryOnError = RetryOnError,
        };
    }

    public override CommandLine.Result GetCommandLine(string executable)
    {
        return CommandLine.ForSync(executable, CreateSettings());
    }
}

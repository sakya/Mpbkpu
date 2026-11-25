using Newtonsoft.Json;
using MpbkpuLib.Interfaces;

namespace MpbkpuLib.FileManagers;

/// <summary>
/// OAuth2 settings for a <see cref="IFileManager"/>
/// </summary>
public class OAuth2Settings : IOAuth2
{
    public string? AppName { get; set; }
    public string? AppId { get; set; }
    [JsonIgnore] // Never logged or saved in the backup database
    public string? AppSecret { get; set; }
    public string? TenantId { get; set; }
    public Uri? ServerUrl { get; set; }
    public Uri? RedirectUrl { get; set; }

    public bool IsEmpty =>
        string.IsNullOrEmpty(AppName) && string.IsNullOrEmpty(AppId) &&
        string.IsNullOrEmpty(AppSecret) && string.IsNullOrEmpty(TenantId) &&
        ServerUrl == null && RedirectUrl == null;
}
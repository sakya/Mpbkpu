namespace MpbkpuLib.Interfaces;

public interface IOAuth2
{
    string? AppName { get; set; }
    string? AppId { get; set; }
    string? AppSecret { get; set; }
    string? TenantId { get; set; }
    Uri? ServerUrl { get; set; }
    Uri? RedirectUrl { get; set; }
}
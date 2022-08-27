using System;

namespace MpbkpuGUI.Abstracts;

public abstract class Job
{
    public class Position
    {
        public string? Path { get; set; }
        public string? User { get; set; }
        public string? Password { get; set; }

        public Uri? OauthServerUrl { get; set; }
        public Uri? OauthRedirectUrl { get; set; }
        public string? OauthAppName { get; set; }
        public string? OauthAppId { get; set; }
        public string? OauthAppSecret { get; set; }
    }

    public string? Name { get; set; }
    public Position? Source { get; set; }
    public Position? Destination { get; set; }
    public DateTime? LastRun { get; set; }
    public string? LogFile { get; set; }
}
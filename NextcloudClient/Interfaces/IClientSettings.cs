using System;

namespace NextcloudClient.Interfaces
{
    public interface IClientSettings
    {
        string AppName { get; set; }
        Uri ServerUri { get; set; }
        string ClientId { get; set; }
        string ClientSecret { get; set; }
        Uri RedirectUri { get; set; }

        /// <summary>
        /// The HTML page to display after the login
        /// </summary>
        string ResponsePage { get; set; }

        string AccessToken { get; set; }
        string RefreshToken { get; set; }
        DateTime? TokenExpiration { get; set; }
    }
}
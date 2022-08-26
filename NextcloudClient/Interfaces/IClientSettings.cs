using System;

namespace NextcloudClient.Interfaces
{
    public interface IClientSettings
    {
        /// <summary>
        /// The OAuth client name
        /// </summary>
        string ClientName { get; set; }
        /// <summary>
        /// The OAuth client id
        /// </summary>
        string ClientId { get; set; }
        /// <summary>
        /// The OAuth client secret
        /// </summary>
        string ClientSecret { get; set; }
        Uri ServerUri { get; set; }
        Uri RedirectUri { get; set; }

        /// <summary>
        /// The HTML page to display after the login
        /// </summary>
        string ResponsePage { get; set; }

        string AccessToken { get; set; }
        string RefreshToken { get; set; }
        DateTime? TokenExpiration { get; set; }

        /// <summary>
        /// The userId (to use basic authentication)
        /// </summary>
        string UserId { get; set; }
        /// <summary>
        /// The user password (to use basic authentication)
        /// </summary>
        string Password { get; set; }
    }
}
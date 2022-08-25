using System;
using NextcloudClient.Interfaces;

namespace NextcloudClient
{
    public class ClientSettings : IClientSettings
    {
        public ClientSettings()
        {
            ResponsePage = @"<html>
<body style=""height: 100%; width: 100%;"">
  <div style=""display: flex; display: flex; justify-content: center;align-items: center;height: 100%;"">
    <h2>You can close this tab</h2>    
  </div>
</body>
</html>";
        }

        public string AppName { get; set; }
        public Uri ServerUri { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public Uri RedirectUri { get; set; }
        public string ResponsePage { get; set; }

        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime? TokenExpiration { get; set; }

        public string UserId { get; set; }
        public string Password { get; set; }
    }
}
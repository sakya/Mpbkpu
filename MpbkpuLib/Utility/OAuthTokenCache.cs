using DeviceId;
using Newtonsoft.Json;

namespace MpbkpuLib.Utility;

public class OAuthTokenCache
{
    #region classes
    public class TokenData
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    internal class TokenCache
    {
        public TokenCache()
        {
            Tokens = new Dictionary<string, TokenData>();
        }

        public Dictionary<string, TokenData> Tokens { get; set; }
    }
    #endregion

    private readonly string _fileName;
    private readonly string _deviceId = new DeviceIdBuilder().AddMachineName().AddMacAddress().AddOsVersion().AddUserName().ToString();

    public OAuthTokenCache(string fileName)
    {
        _fileName = fileName;
    }

    public async Task<TokenData?> GetToken(string account)
    {
        if (string.IsNullOrEmpty(account))
            throw new ArgumentNullException(nameof(account));

        if (!File.Exists(_fileName))
            return null;

        account = account.Trim().ToLower();
        TokenCache? cache;
        await using (var fs = new FileStream(_fileName, FileMode.Open, FileAccess.Read)) {
            using (var sr = new StreamReader(fs)) {
                cache = JsonConvert.DeserializeObject<TokenCache>(await sr.ReadToEndAsync());
            }
        }

        if (cache == null)
            return null;

        var enc = new EncryptDecrypt(_deviceId);
        if (cache.Tokens.TryGetValue(account, out var data)) {
            data.AccessToken = await enc.Decrypt(data.AccessToken!);
            data.RefreshToken = await enc.Decrypt(data.RefreshToken!);

            return data;
        }

        return null;
    }

    public async Task SaveToken(string account, string accessToken, string refreshToken, DateTime? expiresAt)
    {
        if (string.IsNullOrEmpty(account))
            throw new ArgumentNullException(nameof(account));
        if (string.IsNullOrEmpty(accessToken))
            throw new ArgumentNullException(nameof(accessToken));
        if (string.IsNullOrEmpty(refreshToken))
            throw new ArgumentNullException(nameof(refreshToken));
        if (expiresAt == null)
            throw new ArgumentNullException(nameof(expiresAt));

        var dir = Path.GetDirectoryName(_fileName);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir!);

        account = account.Trim().ToLower();
        TokenCache? cache;
        await using (var fs = new FileStream(_fileName, FileMode.OpenOrCreate, FileAccess.Read)) {
            using (var sr = new StreamReader(fs)) {
                cache = JsonConvert.DeserializeObject<TokenCache>(await sr.ReadToEndAsync());
            }
        }

        cache ??= new TokenCache();

        var enc = new EncryptDecrypt(_deviceId);
        var data = new TokenData()
        {
            AccessToken = await enc.Encrypt(accessToken),
            RefreshToken = await enc.Encrypt(refreshToken),
            ExpiresAt = expiresAt
        };

        cache.Tokens[account] = data;
        await using (var fs = new FileStream(_fileName, FileMode.Create, FileAccess.Write)) {
            await using (var sw = new StreamWriter(fs)) {
                await sw.WriteAsync(JsonConvert.SerializeObject(cache));
            }
        }
    }
}
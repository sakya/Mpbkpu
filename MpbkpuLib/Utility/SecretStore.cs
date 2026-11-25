using System.Text;
using Microsoft.Identity.Client.Extensions.Msal;
using Newtonsoft.Json;

namespace MpbkpuLib.Utility;

/// <summary>
/// Key/value secrets saved in the OS secure storage (DPAPI on Windows, Keychain on macOS, keyring on Linux).
/// </summary>
public class SecretStore
{
    private readonly Storage _storage;

    public SecretStore(string directory, string name)
    {
        _storage = Storage.Create(CreateProperties(name, directory, $"Mpbkpu {name}",
            $"Mpbkpu_{name}_service", $"Mpbkpu_{name}_account",
            new KeyValuePair<string, string>("ProductGroup", $"com.github.sakya.{name}")));
    }

    /// <summary>
    /// Create the storage properties shared by the secret store and the OneDrive token cache
    /// </summary>
    public static StorageCreationProperties CreateProperties(string fileName, string directory, string description,
        string macService, string macAccount, KeyValuePair<string, string> linuxAttribute)
    {
        return new StorageCreationPropertiesBuilder(fileName, directory)
            .WithLinuxKeyring(
                "com.github.sakya.Mpbkpu",
                MsalCacheHelper.LinuxKeyRingDefaultCollection,
                description,
                new KeyValuePair<string, string>("Version", "1"),
                linuxAttribute)
            .WithMacKeyChain(macService, macAccount)
            .Build();
    }

    public Dictionary<string, string> Load()
    {
        var data = _storage.ReadData();
        if (data.Length == 0)
            return new Dictionary<string, string>();
        return JsonConvert.DeserializeObject<Dictionary<string, string>>(Encoding.UTF8.GetString(data))
               ?? new Dictionary<string, string>();
    }

    public void Save(IReadOnlyDictionary<string, string> secrets)
    {
        _storage.WriteData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(secrets)));
    }
}

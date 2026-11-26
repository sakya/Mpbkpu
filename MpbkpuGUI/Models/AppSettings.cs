using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MpbkpuGUI.Abstracts;
using MpbkpuLib.Utility;
using Newtonsoft.Json;

namespace MpbkpuGUI.Models;

public class AppSettings
{
    public enum ThemeModes
    {
        /// <summary>
        /// Follow the theme of the operating system
        /// </summary>
        System,
        Light,
        Dark
    }

    public AppSettings()
    {
        Language = "en-US";
        Jobs = new List<Job>();
    }

    public string Language { get; set; }
    public ThemeModes Theme { get; set; } = ThemeModes.System;
    public List<Job> Jobs { get; set; }

    public static async Task<AppSettings?> Load(string fileName)
    {
        await using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
        using var sr = new StreamReader(fs);
        var settings = new JsonSerializerSettings() { TypeNameHandling = TypeNameHandling.Objects };
        var res = JsonConvert.DeserializeObject<AppSettings>(await sr.ReadToEndAsync(), settings);
        if (res == null)
            return null;

        Dictionary<string, string> secrets;
        try {
            secrets = GetSecretStore(fileName).Load();
        } catch {
            // No secure storage (e.g. Linux without a keyring): secrets are not saved
            res.SecretStoreAvailable = false;
            secrets = new Dictionary<string, string>();
        }
        foreach (var job in res.Jobs)
            job.SetSecrets(secrets);
        return res;
    }

    /// <summary>
    /// False when the OS secure storage cannot be used: passwords cannot be remembered
    /// </summary>
    [JsonIgnore]
    public bool SecretStoreAvailable { get; private set; } = true;

    /// <summary>
    /// True if the settings file still contains plain-text secrets (they are removed by <see cref="Save"/>)
    /// </summary>
    [JsonIgnore]
    public bool HasLegacySecrets => Jobs.Any(j => j.HasLegacySecrets);

    public async Task Save(string fileName)
    {
        // Secrets go to the OS secure storage, never to the settings file
        var secrets = new Dictionary<string, string>();
        foreach (var job in Jobs)
            job.GetSecrets(secrets);
        if (SecretStoreAvailable) {
            try {
                GetSecretStore(fileName).Save(secrets);
            } catch {
                // No secure storage: the settings are saved anyway, without secrets
                SecretStoreAvailable = false;
            }
        }

        await using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        await using var sw = new StreamWriter(fs);
        var settings = new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.Objects,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };
        await sw.WriteAsync(JsonConvert.SerializeObject(this, settings));
    }

    private static SecretStore GetSecretStore(string settingsFileName)
    {
        return new SecretStore(Path.GetDirectoryName(Path.GetFullPath(settingsFileName))!, "secrets");
    }
}
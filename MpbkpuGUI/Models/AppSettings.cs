using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Graph;
using Newtonsoft.Json;

namespace MpbkpuGUI.Models;

public class AppSettings
{
    public AppSettings()
    {
        Language = "en-US";
        Jobs = new List<Job>();
    }

    public string Language { get; set; }
    public List<Job> Jobs { get; set; }

    public static async Task<AppSettings?> Load(string fileName)
    {
        await using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
        using var sr = new StreamReader(fs);
        return JsonConvert.DeserializeObject<AppSettings>(await sr.ReadToEndAsync());
    }

    public async Task Save(string fileName)
    {
        await using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        await using var sw = new StreamWriter(fs);
        await sw.WriteAsync(JsonConvert.SerializeObject(this));
    }
}
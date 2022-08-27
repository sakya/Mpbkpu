using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MpbkpuGUI.Models;

public class AppSettings
{
    public AppSettings()
    {
        Jobs = new List<Job>();
    }

    public List<Job> Jobs { get; set; }

    public async Task Save(string fileName)
    {
        await using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        await using var sw = new StreamWriter(fs);
        await sw.WriteAsync(JsonConvert.SerializeObject(this));
    }
}
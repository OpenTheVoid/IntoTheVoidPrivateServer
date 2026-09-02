// Auto-generated from official server pcap capture
// DO NOT EDIT MANUALLY

using System.Collections.Generic;
using System.IO;
using Serilog;

namespace IntoTheVoidServer.Pomelo;

public static class CapturedData
{
    public static readonly Dictionary<string, byte[]> Responses = new();
    public static readonly List<(string route, byte[] data)> Pushes = new();

    public static void Load(string dataDir)
    {
        var respDir = Path.Combine(dataDir, "responses");
        if (Directory.Exists(respDir))
        {
            foreach (var file in Directory.GetFiles(respDir, "*.bin"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var route = name.Replace("_", ".");
                Responses[route] = File.ReadAllBytes(file);
            }
        }

        var pushDir = Path.Combine(respDir, "pushes");
        if (Directory.Exists(pushDir))
        {
            foreach (var file in Directory.GetFiles(pushDir, "*.bin"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var lastUnderscore = name.LastIndexOf('_');
                var route = lastUnderscore > 0
                    ? name.Substring(0, lastUnderscore).Replace("_", ".")
                    : name.Replace("_", ".");
                var data = File.ReadAllBytes(file);
                Pushes.Add((route, data));
            }
        }

        Log.Information("Loaded {RespCount} captured responses and {PushCount} pushes",
            Responses.Count, Pushes.Count);
    }
}

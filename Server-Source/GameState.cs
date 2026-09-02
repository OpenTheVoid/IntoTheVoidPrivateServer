using System.Collections.Concurrent;
using System.Text.Json;

namespace IntoTheVoidServer;

/// <summary>
/// Server-side game state that GM commands can mutate and protocol builders read.
/// Currency keys follow the client's TPSData.Currency enum:
///   1 = Gold, 2 = Diamond(垄金), 14 = BoundDiamond(绑定垄金), 21 = CrystalCredit(晶卷)
///
/// State is persisted to Data/gamestate.json so GM grants survive server restarts.
/// </summary>
public static class GameState
{
    private static readonly ConcurrentDictionary<int, int> Currencies = new();
    private static string _savePath = Path.Combine(AppContext.BaseDirectory, "Data", "gamestate.json");

    /// <summary>Explicit path set at startup (ContentRootPath-relative, not bin-relative).</summary>
    public static void SetSavePath(string contentRoot)
    {
        _savePath = Path.Combine(contentRoot, "Data", "gamestate.json");
    }

    /// <summary>Seed default currencies and load any persisted state on top.</summary>
    public static void InitializeDefaults()
    {
        Currencies.TryAdd(1, 999999);   // Gold
        Currencies.TryAdd(2, 0);        // Diamond
        Currencies.TryAdd(14, 0);       // BoundDiamond
        Currencies.TryAdd(21, 0);       // CrystalCredit (晶卷)

        Load();
    }

    public static int GetCurrency(int type) => Currencies.TryGetValue(type, out var count) ? count : 0;

    /// <summary>
    /// Add count to currency. Returns the new total.
    /// </summary>
    public static int AddCurrency(int type, int count)
    {
        int newVal;
        if (count < 0)
        {
            // Subtraction is allowed (e.g. GM deductions) but never below zero.
            newVal = Math.Max(0, GetCurrency(type) - Math.Abs(count));
            Currencies[type] = newVal;
        }
        else
        {
            newVal = Currencies.AddOrUpdate(type, count, (_, existing) => existing + count);
        }

        Save();
        return newVal;
    }

    /// <summary>Snapshot of all currencies (type -> count).</summary>
    public static Dictionary<int, int> Snapshot()
    {
        return Currencies.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(_savePath)) return;

            var json = File.ReadAllText(_savePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (data == null) return;

            foreach (var (key, value) in data)
            {
                if (int.TryParse(key, out var type))
                {
                    Currencies[type] = value;
                }
            }

            Serilog.Log.Information("[GameState] Loaded persisted state from {Path}: {Entries} entries",
                _savePath, data.Count);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[GameState] Failed to load persisted state from {Path}", _savePath);
        }
    }

    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(
                Snapshot().ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_savePath, json);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[GameState] Failed to persist state to {Path}", _savePath);
        }
    }
}

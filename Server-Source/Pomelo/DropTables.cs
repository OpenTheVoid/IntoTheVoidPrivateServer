using System.Text;
using System.Text.Json;

namespace IntoTheVoidServer.Pomelo;

/// <summary>
/// 掉落相关配置表加载器。
///
/// 数据来源：客户端 bundle（XOR 0x40 解密 + UnityPy 读 MonoBehaviour）导出为 JSON，
/// 导出脚本 <c>memory/_export_drop_tables.py</c>，产物落在 <c>Data/tables/*.json</c>，
/// 随发布一起复制。表内容与客户端 ExcelDataLoader 读到的完全一致。
///
/// 链路（客户端反编译 Assembly-CSharp 全文核对过）：
///   Level[LevelID]._EnemyDrop           -> EnemyDrop[ID]      关卡专属掉落配置（一关一套，910 关用了 280 套）
///   Level[LevelID]._EnemyLoot           -> EnemyLoot[ID]      该关卡可用的刷怪组
///   刷怪请求里的 EnemyLootID             -> EnemyLoot[ID]     本次刷怪组的敌人构成（各类型数量 = 掉落位置数）
///   EnemyDrop[ID]._ItemDropPool{档位}    -> ItemPool[ID]       可嵌套（_PoolType=12 再引 ItemPool；=5 直指 Item）
///   WorldPool[世界池ID]._ItemPool{档位}  -> ItemPool[ID]
///
/// 档位 1..4 对应 EnmType：1=Ordinary(普通) / 2=Elite(精英) / 3=Epic(史诗) / 4=Puny(炮灰)。
/// 客户端 <c>GetDropData()</c> 即按此映射选列。
/// </summary>
public static class DropTables
{
    private const string SubDir = "tables";

    private static readonly object Gate = new();
    private static bool _loaded;
    private static string _loadError = "";

    // JsonDocument 必须保持引用，否则 JsonElement 失效
    private static readonly List<JsonDocument> Docs = new();

    private static Dictionary<int, JsonElement> _enemyDrop = new();
    private static Dictionary<int, JsonElement> _enemyDropNew = new();
    private static Dictionary<int, JsonElement> _enemyDropRate = new();
    private static Dictionary<int, JsonElement> _enemyLoot = new();
    private static Dictionary<int, JsonElement> _itemPool = new();
    private static Dictionary<int, JsonElement> _worldPool = new();
    private static Dictionary<int, JsonElement> _level = new();
    private static Dictionary<int, JsonElement> _item = new();
    private static Dictionary<int, JsonElement> _questShow = new();
    /// <summary>ItemID(道具) -> CurrencyType(货币枚举, 0 表示不是货币)。见 CurrencyLink 表。</summary>
    private static Dictionary<int, int> _currencyOfItem = new();
    /// <summary>ItemType(道具类型) -> CurrencyType；用于识别"面值包"。见 CurrencyLink 表。</summary>
    private static Dictionary<int, int> _currencyOfItemType = new();

    public static bool IsLoaded { get { lock (Gate) { return _loaded; } } }
    public static string LoadError { get { lock (Gate) { return _loadError; } } }

    /// <summary>幂等加载；多次调用只生效一次。失败只记日志不抛异常（掉落降级为空）。</summary>
    public static void EnsureLoaded(string root)
    {
        lock (Gate)
        {
            if (_loaded) return;
            var dir = Path.Combine(root, "Data", SubDir);
            if (!Directory.Exists(dir))
            {
                _loadError = $"目录不存在: {dir}";
                return;
            }

            try
            {
                _enemyDrop = LoadTable(Path.Combine(dir, "EnemyDrop.json"));
                _enemyDropNew = LoadTable(Path.Combine(dir, "EnemyDropNew.json"));
                _enemyDropRate = LoadTable(Path.Combine(dir, "EnemyDropRate.json"));
                _enemyLoot = LoadTable(Path.Combine(dir, "EnemyLoot.json"));
                _itemPool = LoadTable(Path.Combine(dir, "ItemPool.json"));
                _worldPool = LoadTable(Path.Combine(dir, "WorldPool.json"));
                _level = LoadTable(Path.Combine(dir, "Level.json"));
                _item = LoadTable(Path.Combine(dir, "Item.json"));
                // 结算奖励表（Level._RoundRewardShow / Quest._QuestReward -> 888xxxxx 段）
                _questShow = LoadTable(Path.Combine(dir, "QuestShow.json"));
                // 货币映射表：ItemID -> CurrencyType（结算奖励里货币必须走 UpdateInfo.CurrencyData，
                // 而不是 UpdateInfo.Items —— 见 ItemLedger / SettleReward 注释）
                LoadCurrencyLink(Path.Combine(dir, "CurrencyLink.json"));
                _loaded = true;
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
                return;
            }
        }
    }

    private static Dictionary<int, JsonElement> LoadTable(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("掉落表缺失", path);
        var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        Docs.Add(doc);
        var map = new Dictionary<int, JsonElement>(2048);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (int.TryParse(prop.Name, out var id))
                map[id] = prop.Value;
        }
        return map;
    }

    /// <summary>
    /// CurrencyLink.json -> (ItemID -> CurrencyType) 与 (ItemType -> CurrencyType)。
    /// 表行形如 <c>{_ItemID:34180012, _ItemType:1, _CurrencyType:1, _TopCurrencyType:"Gold"}</c>；
    /// <c>_CurrencyType == 0</c> 的行（如 34101002 覆写指令）不是真货币，仍走 UpdateInfo.Items。
    /// </summary>
    private static void LoadCurrencyLink(string path)
    {
        _currencyOfItem = new Dictionary<int, int>(64);
        _currencyOfItemType = new Dictionary<int, int>(64);
        if (!File.Exists(path)) return;
        var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        Docs.Add(doc);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (!prop.Value.TryGetProperty("_CurrencyType", out var ct)) continue;
            if (ct.ValueKind != JsonValueKind.Number || !ct.TryGetInt32(out var currencyType)) continue;
            if (currencyType <= 0) continue;

            // ItemType -> CurrencyType（用于识别"面值包"：34179005 8000个抑制堆栈 type=27 -> 9）
            if (prop.Value.TryGetProperty("_ItemType", out var it)
                && it.ValueKind == JsonValueKind.Number && it.TryGetInt32(out var itemType) && itemType > 0)
                _currencyOfItemType[itemType] = currencyType;

            // ItemID -> CurrencyType（精确命中：34180012 -> 1）
            if (prop.Value.TryGetProperty("_ItemID", out var iid)
                && iid.ValueKind == JsonValueKind.Number && iid.TryGetInt32(out var itemId) && itemId > 0)
                _currencyOfItem[itemId] = currencyType;
        }
    }

    // ---------- 查询 ----------

    /// <summary>
    /// ItemID 对应的货币枚举；&lt;= 0 表示该道具不是货币（走 UpdateInfo.Items）。
    ///
    /// 两步判定：
    ///   1) CurrencyLink 里**精确的 ItemID** 映射（34180012 新联币*1 / 34160001 抑制堆栈）；
    ///   2) 否则看"面值包"——<c>Item._ItemCategory == 9 (Currency)</c> 且它的 <c>_ItemType</c>
    ///      在 CurrencyLink 里有货币映射。典型：34180013「新联币*1000」、34179005
    ///      「8000个抑制堆栈」。这类道具必须走 UpdateInfo.CurrencyData 并按
    ///      <see cref="FaceAmount"/> 折算，否则客户端只会把它当普通道具塞进背包，
    ///      界面上表现为"拿到一个不能用的礼包"。
    /// </summary>
    public static int CurrencyTypeOf(int itemId)
    {
        lock (Gate)
        {
            if (_currencyOfItem.TryGetValue(itemId, out var ct) && ct > 0) return ct;
            if (!_item.TryGetValue(itemId, out var e) || e.ValueKind != JsonValueKind.Object) return 0;
            if (Int(e, "_ItemCategory") != 9) return 0;
            int itemType = Int(e, "_ItemType");
            return _currencyOfItemType.TryGetValue(itemType, out var ct2) ? ct2 : 0;
        }
    }

    /// <summary>
    /// 货币面值：1 个本道具 = N 个货币。
    /// 34180013「新联币*1000」=1000、34179005「8000个抑制堆栈」=8000，
    /// 而 34180012「新联币*1」/34160001「抑制堆栈」=1。
    /// 仅当 Item._Amount 恰好是单元素正整数数组时才采信，否则退回 1（保守）。
    /// </summary>
    public static int FaceAmount(int itemId)
    {
        lock (Gate)
        {
            if (!_item.TryGetValue(itemId, out var e) || e.ValueKind != JsonValueKind.Object) return 1;
            if (!e.TryGetProperty("_Amount", out var p) || p.ValueKind != JsonValueKind.Array) return 1;
            if (p.GetArrayLength() != 1) return 1;
            if (p[0].ValueKind != JsonValueKind.Number || !p[0].TryGetInt32(out var n)) return 1;
            return n > 0 ? n : 1;
        }
    }

    public static JsonElement? EnemyDrop(int id) => Lookup(_enemyDrop, id);
    public static JsonElement? EnemyDropNew(int id) => Lookup(_enemyDropNew, id);
    public static JsonElement? EnemyDropRate(int id) => Lookup(_enemyDropRate, id);
    public static JsonElement? EnemyLoot(int id) => Lookup(_enemyLoot, id);
    public static JsonElement? ItemPool(int id) => Lookup(_itemPool, id);
    public static JsonElement? WorldPool(int id) => Lookup(_worldPool, id);
    public static JsonElement? Level(int id) => Lookup(_level, id);
    public static JsonElement? Item(int id) => Lookup(_item, id);
    public static JsonElement? QuestShow(int id) => Lookup(_questShow, id);

    private static JsonElement? Lookup(Dictionary<int, JsonElement> map, int id)
    {
        lock (Gate)
        {
            return map.TryGetValue(id, out var v) ? v : null;
        }
    }

    public static int Count(string table)
    {
        lock (Gate)
        {
            return table switch
            {
                "EnemyDrop" => _enemyDrop.Count,
                "EnemyDropNew" => _enemyDropNew.Count,
                "EnemyDropRate" => _enemyDropRate.Count,
                "EnemyLoot" => _enemyLoot.Count,
                "ItemPool" => _itemPool.Count,
                "WorldPool" => _worldPool.Count,
                "Level" => _level.Count,
                "Item" => _item.Count,
                "QuestShow" => _questShow.Count,
                _ => 0,
            };
        }
    }

    // ---------- 取值助手（对 null / 类型不符全部容错） ----------

    public static int Int(JsonElement? e, string name, int def = 0)
    {
        if (e is not { } v || v.ValueKind != JsonValueKind.Object) return def;
        if (!v.TryGetProperty(name, out var p)) return def;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n;
        // 个别列在表里是数组（如 Level._EnemyDrop 某些版本），取首元素
        if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() > 0
            && p[0].ValueKind == JsonValueKind.Number && p[0].TryGetInt32(out var n0)) return n0;
        return def;
    }

    public static float Float(JsonElement? e, string name, float def = 0f)
    {
        if (e is not { } v || v.ValueKind != JsonValueKind.Object) return def;
        if (!v.TryGetProperty(name, out var p)) return def;
        if (p.ValueKind == JsonValueKind.Number) return p.GetSingle();
        if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() > 0
            && p[0].ValueKind == JsonValueKind.Number) return p[0].GetSingle();
        return def;
    }

    public static int[] Ints(JsonElement? e, string name)
    {
        if (e is not { } v || v.ValueKind != JsonValueKind.Object) return Array.Empty<int>();
        if (!v.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Array)
            return Array.Empty<int>();
        var arr = new int[p.GetArrayLength()];
        for (int i = 0; i < arr.Length; i++)
            arr[i] = p[i].ValueKind == JsonValueKind.Number && p[i].TryGetInt32(out var n) ? n : 0;
        return arr;
    }

    public static float[] Floats(JsonElement? e, string name)
    {
        if (e is not { } v || v.ValueKind != JsonValueKind.Object) return Array.Empty<float>();
        if (!v.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Array)
            return Array.Empty<float>();
        var arr = new float[p.GetArrayLength()];
        for (int i = 0; i < arr.Length; i++)
            arr[i] = p[i].ValueKind == JsonValueKind.Number ? p[i].GetSingle() : 0f;
        return arr;
    }

    /// <summary>道具展示名（日志用）。</summary>
    public static string ItemName(int itemId)
    {
        var it = Item(itemId);
        if (it is not { } v) return $"<Item {itemId} 不在表内>";
        var name = "";
        if (v.TryGetProperty("_NAME", out var p) && p.ValueKind == JsonValueKind.String)
            name = p.GetString() ?? "";
        return string.IsNullOrEmpty(name) ? $"<Item {itemId}>" : name;
    }

    public static string DumpStats()
    {
        var sb = new StringBuilder();
        sb.Append("EnemyDrop=").Append(Count("EnemyDrop"));
        sb.Append(" EnemyDropNew=").Append(Count("EnemyDropNew"));
        sb.Append(" EnemyDropRate=").Append(Count("EnemyDropRate"));
        sb.Append(" EnemyLoot=").Append(Count("EnemyLoot"));
        sb.Append(" ItemPool=").Append(Count("ItemPool"));
        sb.Append(" WorldPool=").Append(Count("WorldPool"));
        sb.Append(" Level=").Append(Count("Level"));
        sb.Append(" Item=").Append(Count("Item"));
        sb.Append(" QuestShow=").Append(Count("QuestShow"));
        sb.Append(" CurrencyLink=").Append(_currencyOfItem.Count);
        return sb.ToString();
    }
}

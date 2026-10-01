using System.Text.Json;
using Serilog;

namespace IntoTheVoidServer.Pomelo;

/// <summary>
/// 服务端侧的「玩家物品 / 货币持有量账本」。
///
/// ⚠ 为什么必须有它（2026-10-01，用户报「结算栏怎么没东西」）
/// ==========================================================
/// SettleDataResponse.QuestUpdateInfo(UpdateInfo) 里的 <c>BackPackItem.Amount</c> /
/// <c>CurrencyEntity.Count</c>，在客户端语义里是【更新后的持有总量】，**不是本次获得量**。
/// 客户端处处做「新值 − 旧值」求差额（反编译 Lua 全文核对）：
///
///   UserData_Bag:AddItemToCacheByItemList -> AddItemsToCache:
///       if cacheType == Enums.ItemCacheType.ItemID then
///           local haveCount = self:GetItemCountByID(item.ItemID)
///           getCount = wrapItem.Amount - haveCount          -- 净增量，再 LuaGetAnyItem(…, getCount)
///   UserData:GetUpdateCurrencyData:
///       local old = CurrencyData:GetCurrencyNum(currency.CurrencyType)
///       local difference = currency.Count - old             -- 净增量
///   MapProgressLogic:SetQuestUpdateItems -> UserData:GetUpdateItems（结算界面展示）:
///       UserData_Bag:WrapperStuffItems:
///           local amount = data.Amount - haveCount          -- 净增量，<=0 直接丢弃
///
/// 因此服务端若只发「本次获得量」，差额 = 增量 − 玩家既有库存：
///   · 玩家库存为 0 的新物品（首件 mod / 礼包）→ 差额 = 增量，能正常显示；
///   · 玩家本来就有库存的道具（货币、材料、覆写指令…）→ 差额 &lt;= 0，被
///     <c>Util:IsPositiveNumber</c> 过滤掉，界面上一个字都不显示。
///
/// 实测数据（官方抓包 <c>game_game_BackPackListRequest.bin</c> 解析）：
///   玩家覆写指令 34101002 持有 <b>100,270,220</b> 个，而周本 43430305 只发
///   Amount=55000 → 差额 -100,215,220 → 结算栏空。新联币同理。
///
/// 修法：服务端自己维护一份「客户端当前持有量」，下发时给 <b>持有 + 本次获得</b>。
/// 好处是**幂等** —— 客户端重复收到同一个 total 时第二次差额为 0，不会重复加。
///
/// 账本基线
/// ========
///   · 道具：官方抓包 <c>game.game.BackPackListRequest</c> 响应的 ItemList（字段 1..11，
///           跳过 9=TotalCount）
///   · 货币：官方抓包 <c>game.game.PlayerDataRequest</c> 响应 UpdateInfo.CurrencyData
///
/// 客户端每次登录都会重拉 BackPackList（背包被重置回快照值），所以
/// <see cref="ResetFromSnapshot"/> 挂在「回放 BackPackListRequest 捕获响应」上，
/// 两边始终对齐；其余时间账本随我方发放单调累加，并持久化到
/// <c>Data/saves/&lt;uid&gt;/item_ledger.json</c>，服务端重启不丢。
///
/// 去重 / 缓存
/// ============
/// 客户端同一个结算会连发多次 SettleDataRequest（实测 18:38 一口气发了 5 次相同
/// 12 字节请求）。结算奖励含随机项（QuestShow._Rate &lt; 1），若每次都重新 roll，
/// 两次响应内容不同 → 客户端把两份奖励**都**应用，玩家凭空多拿。
/// 因此去重不是放在账本上，而是**把同一个请求的 field6 整段缓存 1.5 秒**，
/// 重复请求原样复用（见 SettleReward.CacheField6）—— 既保证响应完全一致，
/// 也顺带保证账本只累加一次。
///
/// 环境变量 <c>UCS_NO_LEDGER=1</c> 可关闭，回到「直接发本次获得量」的旧行为。
/// </summary>
public static class ItemLedger
{
    private const string BagRoute = "game.game.BackPackListRequest";
    private const string PlayerRoute = "game.game.PlayerDataRequest";

    private static readonly object Gate = new();

    private static Dictionary<int, long> _items = new();
    private static Dictionary<int, long> _currency = new();
    private static string _savePath = "";

    /// <summary>设 UCS_NO_LEDGER=1 则退回旧行为（直接下发本次获得量），便于对比排查。</summary>
    public static bool Disabled =>
        Environment.GetEnvironmentVariable("UCS_NO_LEDGER") == "1";

    public static int ItemKinds { get { lock (Gate) { return _items.Count; } } }
    public static int CurrencyKinds { get { lock (Gate) { return _currency.Count; } } }

    /// <summary>
    /// 登录时切换到该账号的账本。必须在 <c>CapturedData.LoadForPlayer</c> 之后调用
    /// （基线要从该账号的捕获响应里取）。
    /// </summary>
    public static void ActivatePlayer(string root, string uid)
    {
        lock (Gate)
        {
            _savePath = Path.Combine(root, "Data", "saves", uid, "item_ledger.json");
            _items = new Dictionary<int, long>();
            _currency = new Dictionary<int, long>();

            // 基线：官方快照（= 客户端登录后的初始持有量）
            ResetFromSnapshotLocked();
            // 再叠加持久化值（服务端重启场景：客户端没重登，背包仍是累加后的值）
            LoadLocked();
        }

        Log.Information("[ItemLedger] 账号 uid={Uid} 账本就绪: 道具 {Items} 种 / 货币 {Cur} 种",
            uid, ItemKinds, CurrencyKinds);
    }

    /// <summary>
    /// 客户端重拉背包快照（= 客户端本地背包被重置回快照值）→ 账本同步重置。
    /// 由 PomeloTcpServer 在回放 BackPackList 捕获响应时调用。
    /// </summary>
    public static void OnBagSnapshotReplayed()
    {
        lock (Gate)
        {
            ResetFromSnapshotLocked();
            SaveLocked();
        }
        Log.Information("[ItemLedger] 背包快照重放，账本已重置: 道具 {Items} 种 / 货币 {Cur} 种",
            ItemKinds, CurrencyKinds);
    }

    /// <summary>累加道具持有量并返回新的总量。</summary>
    public static long GainItem(int itemId, int gain)
    {
        lock (Gate)
        {
            long cur = (_items.TryGetValue(itemId, out var v) ? v : 0) + gain;
            _items[itemId] = cur;
            return cur;
        }
    }

    /// <summary>累加货币持有量并返回新的总量。</summary>
    public static long GainCurrency(int currencyType, int gain)
    {
        lock (Gate)
        {
            long cur = (_currency.TryGetValue(currencyType, out var v) ? v : 0) + gain;
            _currency[currencyType] = cur;
            return cur;
        }
    }

    public static void Flush()
    {
        lock (Gate) SaveLocked();
    }

    // ------------------------------------------------------------------
    // 内部
    // ------------------------------------------------------------------

    private static void ResetFromSnapshotLocked()
    {
        _items.Clear();
        _currency.Clear();

        if (CapturedData.Responses.TryGetValue(BagRoute, out var bag) && bag != null && bag.Length > 0)
            ParseItemListSnapshot(bag);
        if (CapturedData.Responses.TryGetValue(PlayerRoute, out var pd) && pd != null && pd.Length > 0)
            ParsePlayerDataSnapshot(pd);
    }

    /// <summary>
    /// BackPackListResponse { 1: ItemList }
    /// ItemList: 1=ModItems 2=FrameItems 3=WeaponItems 4=MechaItems 5=ServantItems
    ///           6=TowerItems 7=PetItems 8=Items **9=TotalCount(非物品, 跳过)**
    ///           10=PetSliceItems 11=TimedItems
    /// BackPackItem: 2=ItemID, 3=Amount
    /// </summary>
    private static void ParseItemListSnapshot(byte[] body)
    {
        var top = new ProtoReader(body);
        while (top.TryReadTag(out int fn, out int wt))
        {
            if (fn == 1 && wt == 2)
            {
                ParseItemList(top.ReadBytes());
                continue;
            }
            if (!top.Skip(wt)) break;
        }
    }

    private static void ParseItemList(byte[] itemList)
    {
        var r = new ProtoReader(itemList);
        while (r.TryReadTag(out int fn, out int wt))
        {
            if (wt == 2 && fn != 9)     // 9 = TotalCount，不是物品条目
            {
                ParseBackPackItem(r.ReadBytes());
                continue;
            }
            if (!r.Skip(wt)) break;
        }
    }

    private static void ParseBackPackItem(byte[] item)
    {
        var r = new ProtoReader(item);
        int itemId = 0;
        long amount = 0;
        while (r.TryReadTag(out int fn, out int wt))
        {
            if (fn == 2 && wt == 0) { itemId = (int)r.ReadVarint(); continue; }
            if (fn == 3 && wt == 0) { amount = (long)r.ReadVarint(); continue; }
            if (!r.Skip(wt)) break;
        }
        if (itemId <= 0) return;
        if (amount < 0) amount = 0;
        _items[itemId] = _items.TryGetValue(itemId, out var cur) ? cur + amount : amount;
    }

    /// <summary>
    /// PlayerDataResponse { 1: UpdateInfo }
    /// UpdateInfo: 4 = CurrencyData (repeated CurrencyEntity{ 1=CurrencyType, 2=Count })
    /// </summary>
    private static void ParsePlayerDataSnapshot(byte[] body)
    {
        var top = new ProtoReader(body);
        while (top.TryReadTag(out int fn, out int wt))
        {
            if (fn == 1 && wt == 2)
            {
                ParseUpdateInfoCurrencies(top.ReadBytes());
                continue;
            }
            if (!top.Skip(wt)) break;
        }
    }

    private static void ParseUpdateInfoCurrencies(byte[] updateInfo)
    {
        var r = new ProtoReader(updateInfo);
        while (r.TryReadTag(out int fn, out int wt))
        {
            if (fn == 4 && wt == 2)
            {
                ParseCurrencyEntity(r.ReadBytes());
                continue;
            }
            if (!r.Skip(wt)) break;
        }
    }

    private static void ParseCurrencyEntity(byte[] entity)
    {
        var r = new ProtoReader(entity);
        int type = 0;
        long count = 0;
        while (r.TryReadTag(out int fn, out int wt))
        {
            if (fn == 1 && wt == 0) { type = (int)r.ReadVarint(); continue; }
            if (fn == 2 && wt == 0) { count = (long)r.ReadVarint(); continue; }
            if (!r.Skip(wt)) break;
        }
        if (type <= 0) return;
        if (count < 0) count = 0;
        _currency[type] = count;      // 覆盖语义（官方下发的是最新总量）
    }

    private static void LoadLocked()
    {
        if (string.IsNullOrEmpty(_savePath) || !File.Exists(_savePath)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(_savePath));
            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
                foreach (var p in items.EnumerateObject())
                    if (int.TryParse(p.Name, out var id) && p.Value.TryGetInt64(out var v) && v > 0)
                        _items[id] = v;
            if (doc.RootElement.TryGetProperty("currency", out var cur) && cur.ValueKind == JsonValueKind.Object)
                foreach (var p in cur.EnumerateObject())
                    if (int.TryParse(p.Name, out var id) && p.Value.TryGetInt64(out var v) && v > 0)
                        _currency[id] = v;
            Log.Information("[ItemLedger] 已载入持久化账本 {Path}: 道具 {Items} 种 / 货币 {Cur} 种",
                _savePath, _items.Count, _currency.Count);
        }
        catch (Exception ex)
        {
            Log.Warning("[ItemLedger] 载入账本失败 {Path}: {Msg}", _savePath, ex.Message);
        }
    }

    private static void SaveLocked()
    {
        if (string.IsNullOrEmpty(_savePath)) return;
        try
        {
            var dir = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var payload = new Dictionary<string, object>
            {
                ["items"] = _items.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                ["currency"] = _currency.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            };
            File.WriteAllBytes(_savePath,
                JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch (Exception ex)
        {
            Log.Warning("[ItemLedger] 保存账本失败 {Path}: {Msg}", _savePath, ex.Message);
        }
    }

    /// <summary>极简 protobuf wire-format 读取器（只服务本文件的快照解析）。</summary>
    private sealed class ProtoReader
    {
        private readonly byte[] _b;
        private int _i;

        public ProtoReader(byte[] b) { _b = b; _i = 0; }

        public bool TryReadTag(out int fieldNumber, out int wireType)
        {
            fieldNumber = 0;
            wireType = 0;
            if (_i >= _b.Length) return false;
            ulong tag = ReadVarint();
            fieldNumber = (int)(tag >> 3);
            wireType = (int)(tag & 7);
            return fieldNumber != 0;
        }

        public ulong ReadVarint()
        {
            ulong r = 0;
            int s = 0;
            while (_i < _b.Length)
            {
                byte c = _b[_i++];
                r |= (ulong)(c & 0x7F) << s;
                if ((c & 0x80) == 0) return r;
                s += 7;
                if (s > 63) break;
            }
            return r;
        }

        public byte[] ReadBytes()
        {
            int len = (int)ReadVarint();
            if (len < 0 || _i + len > _b.Length) len = Math.Max(0, _b.Length - _i);
            var slice = new byte[len];
            Array.Copy(_b, _i, slice, 0, len);
            _i += len;
            return slice;
        }

        public bool Skip(int wireType)
        {
            switch (wireType)
            {
                case 0: ReadVarint(); return true;
                case 1: _i += 8; return _i <= _b.Length;
                case 2: ReadBytes(); return true;
                case 5: _i += 4; return _i <= _b.Length;
                default: return false;
            }
        }
    }
}

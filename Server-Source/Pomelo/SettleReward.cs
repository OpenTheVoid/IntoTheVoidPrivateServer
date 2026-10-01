using System.Text;
using System.Text.Json;
using Serilog;

namespace IntoTheVoidServer.Pomelo;

/// <summary>
/// 关卡结算奖励生成器（2026-09-27 新增，同日修正）。
///
/// 背景：通关结算界面（Lua CalculateVM / GameBattle_QuestRewardWindow）显示的奖励
/// 全部来自 SettleDataResponse.QuestUpdateInfo：
///   MapProgressLogic:OnSettleDataEvent(resp) -> SetQuestUpdateItems(resp.QuestUpdateInfo)
///     -> UserData:GetUpdateItems(info.Items)    // ItemList.field8 = repeated BackPackItem
///   CalculateVM:SetQuestUpdateInfo() -> MapProgressLogic:GetQuestUpdateItems()
///
/// 配置链路（客户端配置表实测核对）：
///   Level[LevelID]._RoundRewardShow = int[]，元素为 888xxxxx 段的 QuestShow ID。
///     下标 0  是"怪物掉落"区块（客户端标题 Language 43149909）——其 _ShowReward 是
///             该关怪物掉落池的预览（逐项都能在 EnemyDrop/ItemPool 展开结果里找到）；
///             注意：_RoundRewardShow 长度 > 1 时，[0] 一定是「掉落预览引用行」，
///             其 _Reward 是恒定占位值，**不参与结算发放**（详见 Build 内注释）。
///     下标 1+ 是"关卡结算掉落"/"A轮次掉落"等区块（43149910 / 43149911 …）。
///   QuestShow[showId] { _Reward[], _Amount[], _Rate[] , _ShowReward[], _ShowRewardAmount[] }
///     _Reward/_Amount/_Rate      = 服务端实际发放（_Rate 是发放概率，<1 时按概率判）
///     _ShowReward/_ShowRewardAmount = 客户端关卡信息面板展示的「可获得物品」
///
/// ⚠ 2026-09-27 修正：结算物与面板展示不一致
/// --------------------------------------------------------------
/// 现象：玩家在关卡信息面板看到「关卡结算掉落」列出一堆具体物品（模组/蓝图/材料），
///       通关后实际只拿到「BOSS2随机组合刺杀mod」「周本奖励池」这类【礼包】，
///       两边对不上。
/// 根因：_Reward 里放的是"礼包"道具，而 _ShowReward 列的是【礼包拆开后的内容】。
///       实测证据：
///         Item[34179004 周本奖励池]._LinkID = ItemPool[34357004]
///           内容 = [34179005,34179006,34180394,34180395,34180557,34180901,34179007,34100012]
///           与 QuestShow[88882006]._ShowReward 完全同集合 ✓
///         Item[34179002 BOSS2随机组合刺杀mod]._LinkID = ItemPool[34357002] = 猎杀组合1/2/3 ✓
///         Item[34186005 2-3占领AT2]._LinkID = ItemPool[34346005]
///           内容 = 恰好等于 QuestShow[88880105]._ShowReward 那 10 项 ✓
///       全表统计：被 Level 引用的 418 个 QuestShow 行里，_Reward 项中 241 项是这种"可拆礼包"。
/// 修法：发放时若道具带 Item._LinkID 且指向一个 ItemPool，就按池规则（_DefaultCount/_Weight/
///       _Max/_Priority）拆成具体物品再发放 —— 拆包结果即面板展示的那些物品。
///       **不看 _BestShowReward_MustDrop**（那只是结算界面的展示分组：必掉 / 掉一个 / 随机掉）。
///       池不存在/展开为空则原样发礼包（降级安全）。
///       环境变量 UCS_NO_UNPACK=1 可关闭拆包，回到"发礼包"的旧行为。
///
/// 全量核对（_probe_miss_stat.py，610 个有结算奖励的关卡）：
///   实发项完全落在面板展示列表内的关卡 179（29.3%）→ 加入拆包后 312/556（56.1%）；
///   剩余不匹配项主要是配置本身如此：RoundRewardShow[0] 是"怪物掉落"区块，其
///   _Reward（新联币/抑制堆栈等）本就不是"关卡结算掉落"栏的内容，而是怪物掉落基础产出。
///
/// ⚠ 2026-09-27 二次修正（用户复测仍报"结算还是不对"）
/// --------------------------------------------------------------
/// 现象：周本 43430305 结算实发 = 新联币81000 + 随机裂隙mod + 猎杀组合3 +
///       「8000个抑制堆栈」+ **抑制堆栈x10**；其中"抑制堆栈x10"在关卡面板任何一栏
///       都找不到（面板只展示 88882006 的 12 项 + "怪物掉落"栏的枪械模组），故不匹配。
/// 根因：RoundRewardShow[0] 是【掉落预览引用行】，其 _Reward（恒定 新联币1000+抑制堆栈10）
///       被当成结算奖励一起发了；新联币也因此多出 1000（81000 vs 面板的 80000）。
/// 修法：有效项 > 1 时跳过下标 0（见 Build 注释），只发本关自己的结算奖励行。
///       有效项 == 1 时该行就是本关唯一奖励行，保留。
///
/// ⚠ 2026-10-01 三次修正（用户实测："掉落的裂隙mod应该是随机的…结算只显示一个不能用的裂隙mod"）
/// --------------------------------------------------------------
/// 根因：结算与掉落里出现的「随机裂隙mod 34101107」是**礼包**（cat=16 Material，
///       _LinkID=34310052 -> 6 选 1 的具体执行卡），但被当成最终物品发给了玩家，
///       背包里没有开启入口 ⇒ "不能用"。
/// 修法：**所有带有效 ItemPool 链接的道具一律拆开**，不看 _BestShowReward_MustDrop
///       （那只是结算界面的展示分组）。掉落侧同步修复：DropGenerator.Expand 抽到
///       礼包叶子时也拆（掉落链上 655 个可达叶子中带池的仅 34101107 一个）。
///
/// ⚠ 2026-10-01 四次修正（用户复测："结算奖励的覆写指令怎么又是礼包状态"）
/// --------------------------------------------------------------
/// 根因不在本文件，而在 DropGenerator.Expand —— **ItemPool._RandomType 有两套语义**：
///   Fixed(2)  客户端 LoopItemData.WrapItemPoolData 里 weight/priority 被强制为 1、
///             min=max=_Amount[i]，**_Weight/_Max/_Priority 三列完全不参与计算**。
///   Random(1) 才走 _Min 保底 + _Priority 分层 + _Weight 加权。
///   此前 Expand 对所有池一律走加权抽取 ⇒ Fixed 池（_Weight 列通常全 0）被当成空池。
///   于是「34179006 55000个覆写指令 -> 池34310083 -> 34101002」这类礼包展开为空，
///   TryUnpack 返回 false ⇒ 降级原样发礼包，玩家拿到的还是礼包本体。
///   全表 1181/2843 个 ItemPool 是 Fixed，掉落链可达 39 个、结算链可达 11 个。
/// 修法：Expand 里加 Fixed 分支（每个子项固定产 _Amount[i] 件）+
///       随机分支补 _Min 保底与 sum(min)>count 守卫。
///
/// 下发结构（Google.Protobuf 线上格式）：
///   SettleDataResponse.field6 (0x32) QuestUpdateInfo = UpdateInfo
///     UpdateInfo.field1 (0x0A) Items = ItemList
///       ItemList.field8 (0x42) Items = repeated BackPackItem
///         BackPackItem: 1=SeqID 2=ItemID(0x10) 3=Amount(0x18) 4=GainTimeStamp
/// </summary>
public static class SettleReward
{
    /// <summary>设 UCS_NO_UNPACK=1 则退回"直接发礼包"的旧行为（便于对比排查）。</summary>
    private static readonly bool DisableUnpack = DropGenerator.UnpackDisabled;

    /// <summary>按关卡结算奖励配置 roll 出本次奖励（同 ID 已合并；礼包已拆成具体物品）。</summary>
    public static List<DropItem> Build(int levelId, Random rng)
    {
        var bag = new Dictionary<int, int>();
        var level = DropTables.Level(levelId);
        if (level is null)
            return ToList(bag);

        var shows = DropTables.Ints(level, "_RoundRewardShow").Where(x => x > 0).ToArray();

        // ⚠ 下标 0 是"怪物掉落"栏（Language 43149909），不是结算奖励。
        //   全表核对：RoundRewardShow 有效项 > 1 的关卡，其 [0] 无一例外是
        //     「怪物掉落预览X」「X-Y-Normal-难度N」「经验预览」这类【掉落预览引用行】
        //     —— 名字与关卡本身无关（如 43430305 引用 "2-7-Normal-难度1"、43430304 引用
        //     "2-3占领AT2"），其 _Reward 是恒定占位值（新联币1000 + 抑制堆栈10），
        //     发出去就会在结算界面多出面板上根本没有的"抑制堆栈 x10"，即用户报的"不匹配"。
        //   而 [1..] 才是本关的结算奖励行（"BOSS2任务N结算奖励" / "1-5防御ATx" /
        //     "2.0资源关-经验ATx" 等，名字与关卡对得上），按 RoundType 分 A/B/C 轮。
        //   有效项 == 1 的关卡（292 个）中 [0] 就是它唯一且与本关同名的奖励行
        //     （如 43400001 -> "1-1-Normal-难度1"、反应堆/MR考试系列），必须保留。
        int start = shows.Length > 1 ? 1 : 0;

        for (int i = start; i < shows.Length; i++)
        {
            var show = DropTables.QuestShow(shows[i]);
            if (show is null) continue;

            ApplyShow(show, bag, rng);
        }

        return ToList(bag);
    }

    /// <summary>处理一个 QuestShow 行：按 _Reward/_Amount/_Rate 判定发放。</summary>
    private static void ApplyShow(JsonElement? show, Dictionary<int, int> bag, Random rng)
    {
        var rewards = DropTables.Ints(show, "_Reward");
        var amounts = DropTables.Ints(show, "_Amount");
        var rates = DropTables.Floats(show, "_Rate");

        for (int i = 0; i < rewards.Length; i++)
        {
            int itemId = rewards[i];
            if (itemId <= 0) continue;

            // _Rate 缺失时按 1（必掉）处理，与表里绝大多数行的实际取值一致
            float rate = i < rates.Length ? rates[i] : 1f;
            if (rate <= 0f) continue;
            if (rate < 1f && rng.NextDouble() > rate) continue;

            int amount = i < amounts.Length ? amounts[i] : 1;
            if (amount <= 0) amount = 1;

            // 礼包 -> 一律拆成具体物品再发（与关卡面板展示的"可获得物品"对齐）。
            // 不看 _BestShowReward_MustDrop：那只是结算界面的展示分组（"必掉/掉一个/随机掉"
            // 三个标题），不改变发放语义。34101107 随机裂隙mod 虽在其中，但它
            // _LinkID=34310052 指向 6 选 1 的池，发礼包本体只会得到一个不能用的道具。
            if (!DisableUnpack && TryUnpack(itemId, amount, bag, rng))
                continue;

            bag[itemId] = bag.GetValueOrDefault(itemId) + amount;
        }
    }

    /// <summary>
    /// 道具若是"礼包"（Item._LinkID 指向 ItemPool），按池规则展开 count 次并合并进 bag。
    /// 返回 false 表示不是礼包 / 池不可用 / 展开为空 —— 调用方应原样发放该道具。
    /// </summary>
    private static bool TryUnpack(int itemId, int count, Dictionary<int, int> bag, Random rng)
    {
        var item = DropTables.Item(itemId);
        if (item is null) return false;

        int link = DropTables.Int(item, "_LinkID");
        if (link <= 0) return false;

        var probe = new Dictionary<int, int>();
        for (int k = 0; k < count; k++)
            DropGenerator.ExpandPool(link, probe, rng);
        if (probe.Count == 0) return false;

        foreach (var kv in probe)
            bag[kv.Key] = bag.GetValueOrDefault(kv.Key) + kv.Value;
        return true;
    }

    private static List<DropItem> ToList(Dictionary<int, int> bag)
    {
        var list = new List<DropItem>();
        foreach (var kv in bag)
            if (kv.Key > 0 && kv.Value > 0)
                list.Add(new DropItem(kv.Key, kv.Value));
        list.Sort((a, b) => a.ItemId.CompareTo(b.ItemId));
        return list;
    }

    /// <summary>
    /// 构造 SettleDataResponse 的 field6 (QuestUpdateInfo) 完整字节，含 field tag。
    /// 无奖励时返回空数组（调用方据此整段省略）。
    ///
    /// ⚠ 2026-10-01 五次修正（用户报「结算栏怎么没东西」）
    /// --------------------------------------------------------------
    /// 根因：UpdateInfo 里 <c>BackPackItem.Amount</c> / <c>CurrencyEntity.Count</c> 在客户端
    /// 语义是【更新后持有总量】，客户端处处做「新值 − 旧值」求差额
    /// （见 Pomelo/ItemLedger.cs 顶部长注释，反编译 Lua 全文核对）。
    /// 此前下发的是【本次获得量】，于是「玩家本来就有库存」的道具差额算成 &lt;= 0，
    /// 被客户端 <c>Util:IsPositiveNumber</c> 过滤 → 结算栏全空。
    ///   实测：玩家覆写指令 34101002 持有 100,270,220，而关卡只发 55000。
    ///
    /// 修法：改为下发 <b>账本持有量 + 本次获得</b>（<see cref="ItemLedger"/>）。
    /// 另一并发现在此一并修正：**货币必须走 UpdateInfo.CurrencyData(字段4)**，
    /// 走 Items(字段8) 只会被 <c>AddItemsToCache</c> 按普通道具丢弃，玩家货币根本不会涨。
    /// 货币与道具的对应关系查 CurrencyLink 表（<c>ItemID -&gt; CurrencyType</c>）。
    ///
    /// 下发结构（Google.Protobuf 线上格式）：
    ///   SettleDataResponse.field6 (0x32) QuestUpdateInfo = UpdateInfo
    ///     UpdateInfo.field1 (0x0A) Items = ItemList
    ///       ItemList.field8 (0x42) Items = repeated BackPackItem{ 2=ItemID, 3=Amount }
    ///     UpdateInfo.field4 (0x22) CurrencyData = repeated CurrencyEntity{ 1=CurrencyType, 2=Count }
    ///
    /// 注意：本方法**会写入账本**，所以同一次结算只能调用一次 ——
    /// 客户端重复请求由 <see cref="CacheField6"/> / <see cref="TryGetCachedField6"/> 拦掉。
    /// </summary>
    public static byte[] BuildQuestUpdateInfoField(List<DropItem> items)
    {
        if (items == null || items.Count == 0)
            return Array.Empty<byte>();

        var itemList = ProtoBuilder.Write();
        var currencyEntries = new List<byte[]>();
        bool hasItems = false;

        foreach (var it in items)
        {
            int currencyType = DropTables.IsLoaded ? DropTables.CurrencyTypeOf(it.ItemId) : 0;
            if (currencyType > 0)
            {
                // 货币数量 = 份数 × 面值（"新联币*1000"这种面值包必须折算，
                // 否则玩家只拿到 1 个"礼包"而不是 1000 新联币）。
                int face = DropTables.FaceAmount(it.ItemId);
                long gain = (long)it.Count * face;
                if (gain > int.MaxValue) gain = int.MaxValue;

                // Count = 账本持有量 + 本次获得（关账本时退回"本次获得"）
                long total = ItemLedger.Disabled ? gain : ItemLedger.GainCurrency(currencyType, (int)gain);
                if (total > int.MaxValue) total = int.MaxValue;
                currencyEntries.Add(ProtoBuilder.BuildCurrencyEntity(currencyType, (int)total));
                // 诊断日志（2026-10-01）：客户端展示值 = 下发总量 − 客户端背包持有量，
                // 把三个数都记下来，即可用截图反推出"客户端背包 vs 服务端账本"的真实偏差。
                Log.Information(
                    "[SettleData->field6] 货币 {Id} (type={T} 面值={Face}) 本次获得={Gain} 下发总量={Total} 账本基线={Base}",
                    it.ItemId, currencyType, face, gain, total, total - gain);
            }
            else
            {
                long total = ItemLedger.Disabled
                    ? it.Count
                    : ItemLedger.GainItem(it.ItemId, it.Count);
                if (total > int.MaxValue) total = int.MaxValue;
                var bp = ProtoBuilder.Write();
                bp.WriteInt32(2, it.ItemId);    // BackPackItem.ItemID
                bp.WriteInt32(3, (int)total);   // BackPackItem.Amount = 持有总量
                itemList.WriteMessage(8, bp.ToBytes());   // ItemList.Items
                hasItems = true;
                // 诊断日志（2026-10-01）：同货币，用于反推客户端背包持有量。
                Log.Information(
                    "[SettleData->field6] 道具 {Id} 本次获得={Gain} 下发总量={Total} 账本基线={Base}",
                    it.ItemId, it.Count, total, total - it.Count);
            }
        }

        var updateInfo = ProtoBuilder.Write();
        if (hasItems)
            updateInfo.WriteMessage(1, itemList.ToBytes());       // UpdateInfo.Items
        foreach (var ce in currencyEntries)
            updateInfo.WriteMessage(4, ce);                       // UpdateInfo.CurrencyData

        var outer = ProtoBuilder.Write();
        outer.WriteMessage(6, updateInfo.ToBytes());              // SettleDataResponse.QuestUpdateInfo
        return outer.ToBytes();
    }

    // ------------------------------------------------------------------
    // 同一次结算的响应缓存
    // ------------------------------------------------------------------
    // 客户端对同一次结算会连发多次 SettleDataRequest（实测一口气 5 次，payload 完全相同）。
    // 奖励里含随机项（QuestShow._Rate < 1），若每次重新 roll，两次响应内容不同，
    // 客户端会按"总量差值"把两份奖励**都**入袋。所以这里把生成的 field6 整段
    // 缓存 1.5 秒，同一 key 的重复请求原样复用：
    //   · 响应逐字节一致 → 客户端第二次差值为 0，天然幂等；
    //   · 账本只在首次生成时累加一次。

    private const int CacheWindowMs = 1500;
    private static readonly object CacheGate = new();
    private static string _cacheKey = "";
    private static byte[]? _cacheValue;
    private static long _cacheTicks;

    /// <summary>命中缓存则返回 true 并给出上次生成的 field6 字节。</summary>
    public static bool TryGetCachedField6(string key, out byte[]? field6)
    {
        lock (CacheGate)
        {
            long now = Environment.TickCount64;
            if (_cacheValue != null && key == _cacheKey && now - _cacheTicks < CacheWindowMs)
            {
                field6 = _cacheValue;
                return true;
            }
            field6 = null;
            return false;
        }
    }

    /// <summary>缓存本次生成的 field6（供同一结算的重复请求复用）。</summary>
    public static void CacheField6(string key, byte[] field6)
    {
        lock (CacheGate)
        {
            _cacheKey = key;
            _cacheValue = field6;
            _cacheTicks = Environment.TickCount64;
        }
    }

    /// <summary>日志用摘要："34180012x80000 34101107x1"。</summary>
    public static string Summary(List<DropItem> items)
    {
        if (items == null || items.Count == 0) return "(无结算奖励)";
        var sb = new StringBuilder();
        foreach (var it in items)
        {
            sb.Append(it.ItemId).Append('x').Append(it.Count).Append(' ');
            if (sb.Length > 160) { sb.Append("..."); break; }
        }
        return sb.ToString().TrimEnd();
    }
}

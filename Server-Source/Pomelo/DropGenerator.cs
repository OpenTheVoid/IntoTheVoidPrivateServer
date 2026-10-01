using System.Text;
using System.Text.Json;

namespace IntoTheVoidServer.Pomelo;

/// <summary>单个掉落物（对应 proto 的 ItemInfo：1=ItemID, 2=ItemCount）。</summary>
public sealed record DropItem(int ItemId, int Count);

/// <summary>
/// 一个刷怪组的完整掉落计划：EnmType -> (位置序号 -> 该位置掉什么)。
/// 客户端 RORSpawnManager.ReceiveSpawnerDropNetMsg 拿到响应后按 spawner 存档，
/// 之后该 spawner 每死一只怪，LevelNetDropData.GetEmyDropItem(spawner, emyType)
/// 就按"该类型已击杀计数"作为位置序号取一份 —— 所以位置必须从 0 连续铺满，
/// 且"这一只不掉"要写成空列表占位（缺项会让客户端直接 return）。
/// </summary>
public sealed class SpawnerDropPlan
{
    public Dictionary<int, Dictionary<int, List<DropItem>>> TypeDropList { get; } = new();

    /// <summary>
    /// BOSS 宝箱掉落（对应响应字段 5 BossDrop）。
    /// 客户端把它平铺进 m_bossSpawnerDropList，开箱时按序 GetBossDropItem() 逐份取用，
    /// 所以 key 只需从 0 递增铺满 EnemyLoot._ChestCount 份。
    /// </summary>
    public Dictionary<int, List<DropItem>> BossDrop { get; } = new();

    public int TotalEntries { get; set; }
    public int TotalItems { get; set; }

    public string Summary()
    {
        var sb = new StringBuilder();
        foreach (var (type, byPos) in TypeDropList.OrderBy(k => k.Key))
        {
            int items = byPos.Values.Sum(v => v.Count);
            int nonEmpty = byPos.Values.Count(v => v.Count > 0);
            sb.Append($"t{type}:{nonEmpty}/{byPos.Count}位置({items}件) ");
        }
        if (BossDrop.Count > 0)
        {
            int items = BossDrop.Values.Sum(v => v.Count);
            sb.Append($"宝箱:{items}件 ");
        }
        return sb.Length == 0 ? "(无掉落)" : sb.ToString().TrimEnd();
    }
}

/// <summary>
/// 掉落生成器：按客户端同款算法，从配置表为一次刷怪请求算出掉落清单。
///
/// 复刻自 Assembly-CSharp（反编译）：
///   EnemyLevelSht.GetEmyDropItems()            决定掉几"份" + 选哪个池
///   EnemyDropSht.GetEmyItemsDropNum()          按 _ItemDropRate 加权随机出"份数"
///   LevelManager.GetEmyWorldDropPoolSht()      世界池 vs 关卡池
///   RandomPool.GetDictData / GetSpawnerItems   池递归展开（ItemPool 可套 ItemPool，叶子 _PoolType=5 才是道具）
///   ItemPoolSht.GetItemPoolCount/DefaultCount  每次展开抽几件
///
/// 档位(EnmType) 1=Ordinary 2=Elite 3=Epic 4=Puny，与 EnemyDrop 表的字段后缀一一对应。
/// 每个关卡的 Level._EnemyDrop 指向自己那一行 EnemyDrop，因此掉落天然按关卡对齐。
/// </summary>
public static class DropGenerator
{
    // EnmType（客户端 enum EnmType：Null=0,Ordinary=1,Elite=2,Epic=3,Puny=4）
    public const int TypeOrdinary = 1;
    public const int TypeElite = 2;
    public const int TypeEpic = 3;
    public const int TypePuny = 4;
    // 新体系里宝箱单独一个字段（EnemyDropNew._ChestDropPool），不占 EnmType 枚举
    private const int TypeChest = 5;

    /// <summary>新体系 EnemyDropRate 各 _*DropRate 的基数：100000 = 100%。</summary>
    private const int RateScale = 100000;

    // PoolType（客户端 enum PoolType）
    private const int PoolTypeItemSht = 5;
    private const int PoolTypeItemPoolSht = 12;

    // RandomType（客户端 enum RandomType：Null=0,Random=1,Fixed=2）
    private const int RandomTypeFixed = 2;

    private const int MaxExpandDepth = 8;

    /// <summary>
    /// 生成一次刷怪请求的掉落计划。
    /// </summary>
    /// <param name="levelId">关卡 ID（决定用哪一行 EnemyDrop）</param>
    /// <param name="enemyLootId">刷怪组 ID（决定各类型敌人数量 = 掉落位置数）</param>
    /// <param name="worldPoolTableId">世界池条目 ID（来自 ItemDropWorldPoolResponse 捕获响应，通常 61000002）</param>
    public static SpawnerDropPlan Build(int levelId, int enemyLootId, int worldPoolTableId, Random rng)
    {
        var plan = new SpawnerDropPlan();

        var loot = DropTables.EnemyLoot(enemyLootId);
        if (loot is null)
            return plan;

        var level = DropTables.Level(levelId);
        int enemyDropId = DropTables.Int(level, "_EnemyDrop");

        // 两套掉落体系并存（按 Level._EnemyDrop 落在哪张表决定）：
        //   新体系 EnemyDropNew+EnemyDropRate —— 官方服务端权威掉落
        //   旧体系 EnemyDrop                —— 客户端本地掉落用的历史表
        //
        // ⚠ 2026-09-27 修正（周本/资源关"不掉东西"的真根因）：
        //   两张表的 ID 段有 161 项重叠，而**全部 535 个带配置的关卡，其 Level._EnemyDrop
        //   都能在 EnemyDropNew 里查到**（"只命中 EnemyDrop" 的关卡数为 0）。
        //   EnemyDropNew / EnemyDropRate 在客户端 C# 与 Lua 里都搜不到任何使用点
        //   ⇒ 它们是官方服务端算掉落用的；EnemyDrop 只是客户端本地逻辑的遗留表。
        //   原实现写的是"旧表优先"，重叠关卡因此走了 EnemyDrop：
        //   例 43430305(周本) 旧表普通怪掉率仅 21.66%（实测 9/43 只掉东西），
        //   而新体系 EnemyDropRate 是 Gold 200000(必掉2份) + Material 81%
        //   ⇒ 玩家看到"几乎不掉"。现改为**新体系优先**，旧表仅作兜底。
        var edn = enemyDropId > 0 ? DropTables.EnemyDropNew(enemyDropId) : null;
        var ed = edn is null && enemyDropId > 0 ? DropTables.EnemyDrop(enemyDropId) : null;

        var counts = new (int Type, string Field)[]
        {
            (TypeOrdinary, "_OrdinaryCount"),
            (TypeElite,    "_EliteCount"),
            (TypeEpic,     "_EpicCount"),
            (TypePuny,     "_PunyCount"),
        };

        foreach (var (enmType, field) in counts)
        {
            int n = DropTables.Int(loot, field);
            if (n <= 0)
                continue;

            var byPos = new Dictionary<int, List<DropItem>>(n);
            plan.TypeDropList[enmType] = byPos;

            for (int pos = 0; pos < n; pos++)
            {
                List<DropItem> items = edn is not null
                    ? RollNewSystem(edn, enmType, rng)
                    : RollOldSystem(ed, enmType, worldPoolTableId, rng);

                byPos[pos] = items;      // 位置必须存在；空列表 = 这一只不掉
                plan.TotalEntries++;
                plan.TotalItems += items.Count;
            }
        }

        int chestCount = DropTables.Int(loot, "_ChestCount");
        if (chestCount > 0 && (ed is not null || edn is not null))
        {
            for (int pos = 0; pos < chestCount; pos++)
            {
                List<DropItem> items = edn is not null
                    ? RollNewSystem(edn, TypeChest, rng)
                    : RollOldChest(ed, worldPoolTableId, rng);

                plan.BossDrop[pos] = items;
                plan.TotalItems += items.Count;
            }
        }

        return plan;
    }

    // =================================================================
    // 旧体系：EnemyDrop（375 个关卡）
    // =================================================================
    private static List<DropItem> RollOldSystem(JsonElement? ed, int enmType, int worldPoolTableId, Random rng)
    {
        var items = new List<DropItem>();
        if (ed is null) return items;

        int levelPoolId = DropTables.Int(ed, $"_ItemDropPool{enmType}");
        var rates = DropTables.Floats(ed, $"_ItemDropRate{enmType}");
        var amounts = DropTables.Ints(ed, $"_ItemDropAmount{enmType}");
        float worldRate = DropTables.Float(ed, $"_WorldPoolRate{enmType}");
        int worldPoolId = ResolveWorldPool(worldPoolTableId, enmType);

        int rolls = RollDropCount(amounts, rates, rng);
        if (rolls <= 0) return items;

        int poolId = ChoosePool(levelPoolId, worldPoolId, worldRate, rng);
        if (poolId <= 0) return items;

        var bag = new Dictionary<int, int>();
        for (int k = 0; k < rolls; k++)
            Expand(poolId, bag, rng, 0);

        foreach (var kv in bag)
            if (kv.Key > 0 && kv.Value > 0)
                items.Add(new DropItem(kv.Key, kv.Value));
        return items;
    }

    private static List<DropItem> RollOldChest(JsonElement? ed, int worldPoolTableId, Random rng)
    {
        var items = new List<DropItem>();
        if (ed is null) return items;
        int chestPool = DropTables.Int(ed, "_RewardChestDropPool");
        var chestRates = DropTables.Floats(ed, "_RewardChestDropRate");
        var chestAmounts = DropTables.Ints(ed, "_RewardChestDropAmount");
        float chestWorldRate = DropTables.Float(ed, "_RewardChestWorldPoolRate");
        int worldChestPool = ResolveWorldPool(worldPoolTableId, 0);   // 0 -> _ItemPoolChest

        int rolls = RollDropCount(chestAmounts, chestRates, rng);
        if (rolls <= 0) return items;

        int poolId = ChoosePool(chestPool, worldChestPool, chestWorldRate, rng);
        if (poolId <= 0) return items;

        var bag = new Dictionary<int, int>();
        for (int k = 0; k < rolls; k++)
            Expand(poolId, bag, rng, 0);

        foreach (var kv in bag)
            if (kv.Key > 0 && kv.Value > 0)
                items.Add(new DropItem(kv.Key, kv.Value));
        return items;
    }

    // =================================================================
    // 新体系：EnemyDropNew -> EnemyDropRate（160 个关卡，2.0 章节/资源关）
    //
    //   EnemyDropNew[ID]._NormalDropPool / _EliteDropPool / _EpicDropPool /
    //                    _PunnyDropPool / _ChestDropPool   -> EnemyDropRate[ID]
    //   EnemyDropRate[ID] 把掉落拆成 6 类，每类独立 roll：
    //        _MaterialDrop{Pool,Rate}   材料
    //        _GoldDrop{Pool,Rate}       金币
    //        _ModDrop{Pool,Rate}        模组
    //        _BlueprintDrop{Pool,Rate}  蓝图
    //        _StackDrop{Pool,Rate}      堆叠物
    //        _OthersDrop{Pool,Rate}     其它
    //   **rate 基数是 100000**（实测 _GoldDropRate 只有 0/100000/200000 三种取值
    //   ⇒ 100000 = 必掉 1 份、200000 = 必掉 2 份；_MaterialDropRate 40500 = 40.5%）。
    // =================================================================
    private static readonly (string Pool, string Rate)[] NewCategories =
    {
        ("_MaterialDropPool",  "_MaterialDropRate"),
        ("_GoldDropPool",      "_GoldDropRate"),
        ("_ModDropPool",       "_ModDropRate"),
        ("_BlueprintDropPool", "_BlueprintDropRate"),
        ("_StackDropPool",     "_StackDropRate"),
        ("_OthersDropPool",    "_OthersDropRate"),
    };

    private static List<DropItem> RollNewSystem(JsonElement? edn, int enmType, Random rng)
    {
        var items = new List<DropItem>();
        if (edn is null) return items;

        string field = enmType switch
        {
            TypeOrdinary => "_NormalDropPool",
            TypeElite    => "_EliteDropPool",
            TypeEpic     => "_EpicDropPool",
            TypePuny     => "_PunnyDropPool",
            TypeChest    => "_ChestDropPool",
            _            => "",
        };
        if (field.Length == 0) return items;

        int rateId = DropTables.Int(edn, field);
        if (rateId <= 0) return items;

        var rate = DropTables.EnemyDropRate(rateId);
        if (rate is null) return items;

        var bag = new Dictionary<int, int>();
        foreach (var (poolField, rateField) in NewCategories)
        {
            int poolId = DropTables.Int(rate, poolField);
            int r = DropTables.Int(rate, rateField);
            if (poolId <= 0 || r <= 0) continue;

            int rolls = RollRate(r, rng);
            for (int k = 0; k < rolls; k++)
                Expand(poolId, bag, rng, 0);
        }

        foreach (var kv in bag)
            if (kv.Key > 0 && kv.Value > 0)
                items.Add(new DropItem(kv.Key, kv.Value));
        return items;
    }

    /// <summary>
    /// 新体系份数：rate / 100000 是必掉份数，余数 rate % 100000 是"再多一份"的概率。
    /// 例：100000 -> 必掉 1 份；200000 -> 必掉 2 份；40500 -> 40.5% 掉 1 份。
    /// </summary>
    private static int RollRate(int rate, Random rng)
    {
        if (rate <= 0) return 0;
        int whole = rate / RateScale;
        int rem = rate % RateScale;
        if (rem > 0 && rng.Next(RateScale) < rem) whole++;
        return whole;
    }

    // ---------------------------------------------------------------
    // 份数：EnemyDropSht.GetEmyItemsDropNum()
    //   以 _ItemDropAmount 作候选值、_ItemDropRate 作权重加权随机
    // ---------------------------------------------------------------
    private static int RollDropCount(int[] amounts, float[] rates, Random rng)
    {
        var candidates = new List<(int Value, double Weight)>();
        for (int i = 0; i < amounts.Length; i++)
        {
            if (i >= rates.Length) break;
            if (rates[i] == 0f) continue;
            candidates.Add((amounts[i], rates[i]));
        }
        if (candidates.Count == 0)
            return 0;
        return WeightedPick(candidates, rng);
    }

    // ---------------------------------------------------------------
    // 世界池 vs 关卡池：SelectWorldOrLevelDropPoolSht()
    // ---------------------------------------------------------------
    private static int ChoosePool(int levelPoolId, int worldPoolId, float worldRate, Random rng)
    {
        if (worldPoolId <= 0) return levelPoolId;
        if (levelPoolId <= 0) return worldPoolId;
        return rng.NextDouble() <= worldRate ? worldPoolId : levelPoolId;
    }

    /// <summary>WorldPool[世界池ID]._ItemPool{档位} -> 具体道具池 ID（enmType=0 取宝箱池）。</summary>
    private static int ResolveWorldPool(int worldPoolTableId, int enmType)
    {
        var wp = DropTables.WorldPool(worldPoolTableId);
        if (wp is null) return 0;
        return enmType switch
        {
            TypeOrdinary => DropTables.Int(wp, "_ItemPoolOrdinary"),
            TypeElite    => DropTables.Int(wp, "_ItemPoolElite"),
            TypeEpic     => DropTables.Int(wp, "_ItemPoolEpic"),
            TypePuny     => DropTables.Int(wp, "_ItemPoolPuny"),
            _            => DropTables.Int(wp, "_ItemPoolChest"),
        };
    }

    // =================================================================
    // 礼包拆包（Item._LinkID -> ItemPool）
    //
    // 配置语义：道具若带 _LinkID 且该 ID 能在 ItemPool 表里查到，它就是"礼包"，
    //   内容 = 那个池按自身规则（_DefaultCount/_Weight/_Max/_Priority）展开的结果。
    //   实测闭环：34101107 随机裂隙mod -> 池34310052 = 6 选 1
    //       (34101100 步枪 / 34101101 霰弹枪 / 34101102 手枪 / 34101103 炮 /
    //        34101104 近战 执行卡 + 34101115 混淆 具装)；
    //     34179004 周本奖励池 -> 池34357004 = 结算面板展示的那 8 项。
    //
    // ⚠ 2026-10-01 修正：**礼包必须拆开发放**。
    //   现象（用户实测）：结算界面只显示一个"随机裂隙mod"，进背包后不能用
    //   —— 它是 cat=16(Material) 的纯礼包，客户端没有"开启"入口。
    //   规则：不看 _BestShowReward_MustDrop（那只是结算界面的展示分组，不影响发放），
    //         只要带有效 ItemPool 链接就按池展开；池不存在/展开为空则原样发（降级安全）。
    //   影响面：掉落链上 655 个可达叶子里带池的只有 34101107 一个，改动可控。
    //
    // ⚠ 2026-10-01 补充：上面这些礼包池（34310052 / 34357004 / 34346005）都是
    //   RandomType=1(Random)，能正常展开；但**结算/掉落里还有一批 RandomType=2(Fixed)
    //   的礼包池**（如 34179006 55000个覆写指令 -> 34310083、34180707 覆写指令*6000 ->
    //   34310084、34179014 25000个覆写指令 -> 34310090），它们的 _Weight 列全 0，
    //   在修 Expand 的 Fixed 分支之前会被判成空池 ⇒ 礼包拆不开、又原样发出去。
    // =================================================================

    /// <summary>设 UCS_NO_UNPACK=1 则退回"直接发礼包"的旧行为（便于对比排查）。</summary>
    public static readonly bool UnpackDisabled =
        Environment.GetEnvironmentVariable("UCS_NO_UNPACK") == "1";

    /// <summary>
    /// 把一个道具池展开进 bag（结算奖励的"礼包"拆包用）。
    /// 返回 true 表示池存在且至少产出了一件物品。
    /// </summary>
    public static bool ExpandPool(int poolId, Dictionary<int, int> bag, Random rng)
        => ExpandInto(poolId, bag, rng, 0);

    private static bool ExpandInto(int poolId, Dictionary<int, int> bag, Random rng, int depth)
    {
        if (poolId <= 0) return false;
        var p = DropTables.ItemPool(poolId);
        if (p is null) return false;
        int poolType = DropTables.Int(p, "_PoolType");
        if (poolType != PoolTypeItemSht && poolType != PoolTypeItemPoolSht) return false;

        int before = CountBag(bag);
        Expand(poolId, bag, rng, depth);
        return CountBag(bag) > before;
    }

    /// <summary>
    /// 掉落池里抽到的叶子若是"礼包"，就地拆开（同 ExpandPool，但带深度保护）。
    /// 返回 false 表示不是礼包 / 池不可用 / 展开为空 —— 调用方应原样发放。
    /// </summary>
    private static bool TryUnpackLeaf(int itemId, Dictionary<int, int> bag, Random rng, int depth)
    {
        if (depth > MaxExpandDepth) return false;

        var item = DropTables.Item(itemId);
        if (item is null) return false;

        int link = DropTables.Int(item, "_LinkID");
        if (link <= 0) return false;

        return ExpandInto(link, bag, rng, depth);
    }

    private static int CountBag(Dictionary<int, int> bag)
    {
        int n = 0;
        foreach (var v in bag.Values) n += v;
        return n;
    }

    // ---------------------------------------------------------------
    // 池展开：RandomPool.GetDictData() / GetSpawnerItems() 的等价实现
    //   _PoolType=5  -> _SubItem 是道具 ID，直接产出
    //   _PoolType=12 -> _SubItem 是下层 ItemPool ID，递归展开
    //
    // 两条完全不同的分支，由 _RandomType 决定：
    //   Fixed(2)   —— 每个子项固定产 _Amount[i] 件（见下）
    //   Random(1)/Null(0) —— _Min 保底 + _Priority 分层 + _Weight 加权
    // ---------------------------------------------------------------
    private static void Expand(int poolId, Dictionary<int, int> bag, Random rng, int depth)
    {
        if (depth > MaxExpandDepth) return;

        var p = DropTables.ItemPool(poolId);
        if (p is null) return;

        int poolType = DropTables.Int(p, "_PoolType");
        if (poolType != PoolTypeItemSht && poolType != PoolTypeItemPoolSht)
            return;   // 其它类型（实体/遗物/房间…）不走道具掉落

        var sub = DropTables.Ints(p, "_SubItem");
        if (sub.Length == 0) return;

        // =============================================================
        // Fixed(2)：固定产出
        //   客户端 LoopItemData.WrapItemPoolData 在 Fixed 分支里**完全忽略
        //   _Weight/_Max/_Priority**，把每项强制成 weight=1 / priority=1、
        //   min=max=_Amount[i] ⇒ 每个子项固定产 _Amount[i] 件。
        //
        // ⚠ 2026-10-01 修正（用户反馈"结算奖励的覆写指令怎么又是礼包状态"）：
        //   此前所有池一律走"加权抽取"，而 Fixed 池的 _Weight 列通常全 0
        //   （如 34310083「55000个覆写指令」→ 34101002、34330376 → 随机裂隙mod），
        //   于是 PickChild 永远返回 -1 ⇒ 整池被当成空池 ⇒
        //     ① 结算/掉落的礼包拆不开（展开为空 ⇒ 降级原样发礼包）；
        //     ② 掉落链上 Fixed 池漏产（如 34330376 本该掉出随机裂隙mod）。
        //   全表 1181/2843 个 ItemPool 是 Fixed，必须按 Fixed 语义处理。
        // =============================================================
        if (DropTables.Int(p, "_RandomType") == RandomTypeFixed)
        {
            var fixedAmounts = DropTables.Ints(p, "_Amount");
            // 客户端：Amount 为空或与 SubItem 长度不符 ⇒ 报错、整池不产出
            if (fixedAmounts.Length != sub.Length) return;

            for (int i = 0; i < sub.Length; i++)
                EmitChild(sub[i], poolType, fixedAmounts[i], bag, rng, depth);
            return;
        }

        // =============================================================
        // Random(1) / Null(0)：_Min 保底 + _Priority 分层 + _Weight 加权
        // =============================================================
        var weights = Fit(DropTables.Ints(p, "_Weight"), sub.Length, 0);
        var maxes = Fit(DropTables.Ints(p, "_Max"), sub.Length, -1);      // -1 = 不限次数
        var prios = Fit(DropTables.Ints(p, "_Priority"), sub.Length, 1);
        var mins = Fit(DropTables.Ints(p, "_Min"), sub.Length, 0);        // 保底件数

        int rolls = PoolRollCount(p, rng);
        if (rolls <= 0) return;

        // GetSpawnerItems(): sum(min) > count ⇒ 整池作废（客户端 return null）
        int minSum = 0;
        for (int i = 0; i < sub.Length; i++)
            if (mins[i] > 0) minSum += mins[i];
        if (minSum > rolls) return;

        var spawned = new int[sub.Length];
        int produced = 0;

        // 保底：每项先无条件给 _Min 件（offsetMinCount 分支，同时计入 hadSpawnerCount）
        for (int i = 0; i < sub.Length; i++)
        {
            int m = mins[i];
            if (m <= 0) continue;
            spawned[i] += m;
            produced += m;
            EmitChild(sub[i], poolType, m, bag, rng, depth);
        }

        // 余量按 priority + weight 抽（PrivorityFilter 每轮只出一个）
        while (produced < rolls)
        {
            int idx = PickChild(sub, weights, maxes, prios, spawned, rng);
            if (idx < 0) break;
            spawned[idx]++;
            produced++;
            EmitChild(sub[idx], poolType, 1, bag, rng, depth);
        }
    }

    /// <summary>
    /// 产出池的一个子项：嵌套池递归展开、礼包就地拆包、叶子进 bag。
    /// count 只对叶子生效；嵌套池的件数由子池自身规则决定
    /// （客户端 RandomPool.GetPoolSpawnerCount 会丢弃父级件数，每个子池只展开一次）。
    /// </summary>
    private static void EmitChild(int childId, int poolType, int count, Dictionary<int, int> bag, Random rng, int depth)
    {
        if (childId <= 0 || count <= 0) return;

        if (poolType == PoolTypeItemPoolSht)
        {
            Expand(childId, bag, rng, depth + 1);
            return;
        }

        // 叶子若是"礼包"（Item._LinkID 指向 ItemPool），就地拆开再发。
        // 例：34101107 随机裂隙mod -> 34101100/34101101/…/34101115 之一；
        //     34179006 55000个覆写指令 -> 34101002 刷新mod道具洗练点数覆写指令 x55000。
        if (!UnpackDisabled && TryUnpackLeaf(childId, bag, rng, depth + 1))
            return;

        bag[childId] = bag.GetValueOrDefault(childId) + count;
    }

    /// <summary>
    /// ItemPoolSht.GetItemPoolCount() / GetItemPoolShtDefaultCount()
    ///   Fixed  -> 用 _Amount 累加（固定件数）
    ///   其它    -> _DefaultCount 是 [min,max] 时区间随机，单元素时取该值
    /// </summary>
    private static int PoolRollCount(System.Text.Json.JsonElement? p, Random rng)
    {
        if (DropTables.Int(p, "_RandomType") == RandomTypeFixed)
        {
            int sum = 0;
            foreach (var a in DropTables.Ints(p, "_Amount"))
                sum += a;
            return sum;
        }

        var dft = DropTables.Ints(p, "_DefaultCount");
        if (dft.Length >= 2)
        {
            int lo = dft[0], hi = dft[1];
            if (hi < lo) (lo, hi) = (hi, lo);
            return lo == hi ? lo : rng.Next(lo, hi + 1);   // 含上界
        }
        return dft.Length == 1 ? dft[0] : 0;
    }

    /// <summary>
    /// RandomPool.PrivorityFilter(): 先按 priority 分层（小者优先），
    /// 层内按 weight 加权随机；已抽满 _Max 次的子项剔除。
    /// </summary>
    private static int PickChild(int[] sub, int[] weights, int[] maxes, int[] prios, int[] spawned, Random rng)
    {
        int bestPrio = int.MaxValue;
        for (int i = 0; i < sub.Length; i++)
        {
            if (weights[i] <= 0) continue;
            if (maxes[i] >= 0 && spawned[i] >= maxes[i]) continue;
            if (prios[i] < bestPrio) bestPrio = prios[i];
        }
        if (bestPrio == int.MaxValue) return -1;

        double total = 0;
        for (int i = 0; i < sub.Length; i++)
        {
            if (weights[i] <= 0 || prios[i] != bestPrio) continue;
            if (maxes[i] >= 0 && spawned[i] >= maxes[i]) continue;
            total += weights[i];
        }
        if (total <= 0) return -1;

        double roll = rng.NextDouble() * total;
        double acc = 0;
        int last = -1;
        for (int i = 0; i < sub.Length; i++)
        {
            if (weights[i] <= 0 || prios[i] != bestPrio) continue;
            if (maxes[i] >= 0 && spawned[i] >= maxes[i]) continue;
            last = i;
            acc += weights[i];
            if (roll < acc) return i;
        }
        return last;
    }

    /// <summary>WeightedRandomizer.GetRandom(): 前缀和 + 均匀随机，标准加权抽样。</summary>
    private static int WeightedPick(List<(int Value, double Weight)> items, Random rng)
    {
        double total = 0;
        foreach (var it in items) total += it.Weight;
        if (total <= 0) return items.Count > 0 ? items[0].Value : 0;

        double roll = rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < items.Count; i++)
        {
            acc += items[i].Weight;
            if (roll < acc) return items[i].Value;
        }
        return items[^1].Value;
    }

    private static int[] Fit(int[] arr, int len, int def)
    {
        if (arr.Length == len) return arr;
        var r = new int[len];
        for (int i = 0; i < len; i++)
            r[i] = i < arr.Length ? arr[i] : def;
        return r;
    }
}

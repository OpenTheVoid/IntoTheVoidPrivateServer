# IntoTheVoid Private Server · 驱入虚空本地私服

> 让官方《驱入虚空》客户端跑成**完全离线本地私服**。
> **免改 hosts、免装证书、游戏本体零修改** —— 所有改动都在 BepInEx 插件 + 本地服务端内完成。

![state](https://img.shields.io/badge/state-可登录·可进关卡·掉落与结算正常-brightgreen)
![client](https://img.shields.io/badge/client-官方原版-blue)
![license](https://img.shields.io/badge/license-仅供学习研究-lightgrey)

---

## 目录

- [当前能力](#当前能力)
- [包内容与目录结构](#包内容与目录结构)
- [快速开始（4 步）](#快速开始4-步)
- [详细使用方法](#详细使用方法)
- [GM 管理面板](#gm-管理面板)
- [从源码构建](#从源码构建)
- [工作原理（免改 hosts 是怎么做到的）](#工作原理)
- [服务端功能实现](#服务端功能实现)
- [客户端接口对齐（热更程序集）](#客户端接口对齐热更程序集)
- [常见问题 FAQ](#常见问题-faq)
- [免责声明](#免责声明)

---

## 当前能力

| 项 | 状态 |
|---|---|
| 免改 hosts 登录 | ✅ 插件内存级拦截，系统 hosts 完全不动 |
| 登录 / 进主城 | ✅ 实测通过 |
| 进关卡（战斗 / 副本） | ✅ 实测通过 |
| 关卡怪物掉落（服务端权威） | ✅ 掉落表完整复刻，含世界池与 BOSS 宝箱 |
| 关卡结算奖励（道具 + 货币） | ✅ 支持礼包自动拆包、货币面值折算 |
| 悖域巡查（突击警报）进度推进 | ✅ 含进度推送 |
| 悖域回归（周本）进度推进 | ✅ 含解锁推送 |
| GM 管理面板 | ✅ 发货币 / 查在线玩家 / 自定义路由测试 |
| 账号系统 | ✅ 建号 / 多账号 / 存档隔离 / 备份还原 |
| 联机 | ❌ 插件把服务器地址固定为 `127.0.0.1`，**只能单机** |

> **本包不含游戏本体**（`IntoTheVoid.exe` / `GameAssembly.dll` / `UnityPlayer.dll` / 游戏资源等）。
> 你需要一套自己的官方客户端。

---

## 包内容与目录结构

```
IntoTheVoidPrivateServer/
├── README.md                       # 本文件
├── .gitignore
├── Server/                         # 服务端【发布版，开箱即用】
│   ├── IntoTheVoidServer.exe       # 主程序（双击即运行）
│   ├── Data/responses/             # 官方抓包回放数据（73 响应 + 5 推送，运行必需）
│   ├── Data/tables/                # 掉落/结算配置表（12 张，运行必需）
│   ├── Data/gamestate.json         # 初始状态（可编辑）
│   ├── wwwroot/admin/              # GM 管理面板页面
│   ├── cert.pfx / cert.cer         # HTTPS 自签证书（SAN 覆盖官方域名）
│   ├── rsa_private_key.txt         # 服务端登录签名 RSA 私钥
│   └── pw_rsa_private_key.txt      # 登录口令解密私钥
├── Server-Source/                  # 服务端【源码版，可二次开发】
│   ├── IntoTheVoidServer.csproj    # (编译需 .NET 10 SDK)
│   ├── Program.cs / GamePaths.cs / GameState.cs
│   ├── Accounts/                   # 账号库 + 存档库
│   ├── Http/                       # 登录 / CDN / GM 面板 / 渠道接口
│   ├── Net/                        # Pomelo TCP 网关
│   ├── Pomelo/                     # 协议编解码 + 各业务响应
│   ├── Router/                     # 请求路由
│   ├── HotAssembly/                # 客户端热更程序集（协议对齐的唯一依据）
│   └── tools/                      # 协议提取 / 缺口分析脚本
├── Client-Plugin/                  # 客户端【BepInEx 插件包，免改 hosts 的核心】
│   ├── BepInEx/                    # core + plugins(UseCustomServer) + config
│   ├── dotnet/                     # IL2CPP 所需 .NET 运行时（约 67MB）
│   ├── winhttp.dll                 # BepInEx 注入器
│   └── doorstop_config.ini / .doorstop_version
├── Launcher/                       # 登录器【建号 + 启服 + 存档管理】
│   ├── publish/驱入虚空登录器.exe  # 已编译版（框架依赖，约 240KB）
│   └── *.cs / LoginLauncher.csproj # 源码
└── docs/
    ├── 使用说明.md                 # 简明版上手文档
    ├── 一键安装.bat                # 合并插件 + 启动服务端（可选）
    └── update_hosts.bat            # hosts 兜底脚本（可选，正常用不到）
```

**这个仓库不覆盖官方客户端的任何原文件**——`Client-Plugin/` 里全部是游戏本体本来没有的新增件，
安装 = 往游戏目录里新增文件；卸载 = 删掉这些新增件即可，游戏本体毫发无损。

---

## 快速开始（4 步）

### 前提

1. 一套**干净的官方成品客户端**（含 `IntoTheVoid.exe`）
2. **.NET 10 运行时**，见下表

| 组件 | 需要的运行时 |
|---|---|
| `Server/IntoTheVoidServer.exe` | **ASP.NET Core Runtime 10** |
| `Launcher/publish/驱入虚空登录器.exe` | **.NET Desktop Runtime 10 (x64)** |

> 最省事的做法：直接安装 **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** 或
> **[.NET 10 Runtime (Desktop + ASP.NET Core) x64](https://dotnet.microsoft.com/download/dotnet/10.0)**，
> 一次装齐两种运行时。

---

### 第 1 步：部署客户端插件

把 `Client-Plugin/` 里的**全部文件**拷贝到游戏根目录（与 `IntoTheVoid.exe` 同级），合并：

```
游戏根目录/
├── IntoTheVoid.exe          (官方原有，不动)
├── IntoTheVoid_Data/        (官方原有，不动)
├── BepInEx/                 ← 新增
├── dotnet/                  ← 新增
├── winhttp.dll              ← 新增
├── doorstop_config.ini      ← 新增
└── .doorstop_version        ← 新增
```

> BepInEx 首次启动会自动生成 `BepInEx/interop/` 与 `BepInEx/unity-libs/`。
> 本包为减小体积**未包含**这两个目录，首次进游戏会多等几分钟（生成一次即可，之后正常）。

---

### 第 2 步：建一个账号

服务端**不含**任何建号接口，首次使用必须先用登录器建号（否则登录会提示「账号不存在」）。

双击 `Launcher/publish/驱入虚空登录器.exe`：

1. 顶部「服务端目录」若为空或识别错误，点 **浏览...** 选到本仓库的 **`Server`** 目录
   （即含 `IntoTheVoidServer.exe` 的那一层）
2. 点右上 **新建账号**
   - **账号**：3–11 位**纯数字**
   - **密码**：6–32 位可见 ASCII 字符（不含空格和中文）
   - **初始存档**：选「沿用原存档」可按官方抓包快照初始化；选「全新空档」则从零开始
3. 建好后账号会出现在列表里

---

### 第 3 步：启动服务端

在登录器里点 **启动服务端**，或直接双击 `Server/IntoTheVoidServer.exe`。

控制台首屏出现以下内容即为正常：

```
Loaded 73 captured responses and 5 pushes     ← 0 则说明 Data/responses 缺失
Now listening on: http://0.0.0.0:80 / 443 / 8183 ...
```

> 服务端监听 **HTTP 80 / HTTPS 443 / 8183(管理) / TCP 30531(网关)**。
> ⚠️ 80 / 443 必须空闲 —— 若开着 **Watt Toolkit（Steam++）** 之类的加速器，请先退出。

---

### 第 4 步：进游戏

双击 `IntoTheVoid.exe`（也可在登录器里点 **▶ 启动游戏（自动拉起服务端）**，一条龙）。

启动后：
- 登录界面切到 **官方账号登录**
- 输入第 2 步建的账号密码 → 登录 → 自动进主城

---

## 详细使用方法

### 验证是否成功

- 登录后自动进入主城，可打开**关卡界面进入战斗关卡**
- 服务端控制台会持续打印客户端的 HTTP/TCP 请求与回放命中记录
- GM 面板：浏览器打开 <http://127.0.0.1:8183/admin/>

> 控制台会**持续刷屏**心跳日志（每几秒一条），这是正常现象，不是报错。

### 登录器还能做什么

| 功能 | 说明 |
|---|---|
| 启动服务端 / 停止服务端 | 不用去开黑框 |
| ▶ 启动游戏（自动拉起服务端） | 一键：服务端 + 游戏一起起 |
| 新建账号 / 删除账号 | 删除账号时其存档目录 `Data/saves/<uid>` 会保留 |
| 备份存档 / 还原存档 | 从 `Data/backups/` 选择历史备份覆盖当前存档 |
| 打开存档目录 / 打开备份目录 | 直达资源管理器 |

### 更换磁盘 / 游戏目录移动后

服务端会自动向上探测 `IntoTheVoid.exe` 定位游戏根目录；探测不到时设置环境变量：

```
UCS_GAME_ROOT=D:\你的游戏目录
```

登录器的服务端目录若探测失败，在顶部输入框手动选区即可（会自动记住）。

### 服务端日志

- 发布版：`Server/logs/server_YYYYMMDD.log`（同一天第二次启动会写成 `_001` 后缀）
- 源码运行版：`Server-Source/logs/`

### 存档位置

```
Server/Data/
├── accounts.json              # 账号库
└── saves/<uid>/               # 每个账号一份
    ├── responses/             # 该账号的世界状态快照
    ├── gamestate.json         # 货币等
    └── *.json                 # 事件模式进度（alert_progress / weekly_progress 等）
```

> 想要干净重来：停掉服务端，删掉 `Server/Data/saves/`、`accounts.json`，重启即可。

---

## GM 管理面板

浏览器打开 **<http://127.0.0.1:8183/admin/>**

- **货币发放**：选择货币类型（晶卷 CrystalCredit=21 / 金币 Gold=1 / 抑制堆栈 ModStack=9 等），
  输入数量发放；发放后会立即推送在线客户端刷新，重登后仍保留
- **查询在线玩家**：查看当前连接玩家的 UID 与状态
- **自定义路由测试**：手动构造 `route` 与字段编号，用于排查接口

> ⚠️ **长期进度请落盘**：货币等状态若只存在内存（GameState），服务端重启后会归零。
> 可编辑 `Server/Data/gamestate.json` 预设初始值。

---

## 从源码构建

### 服务端

```bash
# 需要 .NET 10 SDK
cd Server-Source
dotnet restore
dotnet publish -c Release -o publish
# csproj 已配置自动复制 Data/responses 与 Data/tables
```

### 登录器

```bash
# 需要 .NET 10 SDK（含 Windows Desktop）
cd Launcher
dotnet restore
dotnet publish -c Release -o publish
# 产物：publish/驱入虚空登录器.exe
```

> 登录器通过 `<Compile Include="..\Server-Source\Accounts\*.cs">` **直接复用服务端源码**，
> 保证两侧账号/存档数据格式永远一致。

### 发布为无依赖版本（可选）

若接收方不想装运行时，把上面的 `-o publish` 换成：

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

产物自带上百 MB 运行时，双击即用。服务端与登录器各自发布一次。

---

## 工作原理

免改 hosts、免装证书由 **BepInEx 插件（UseCustomServer）** 在进程内完成，系统 hosts 完全不用动：

| 官方行为 | 本地处理 | 手段 |
|---|---|---|
| HTTP(S) 请求 `*.jinzhangshu.com` | 解析回 `127.0.0.1` | 插件 Hook `Dns.GetHostAddresses`，官方域一律返回本地回环地址 |
| HTTPS 443 | 本地 Kestrel 自签证书应答 | 证书 SAN 已覆盖全部官方域；客户端 BestHTTP 默认放行自签证书，**无需安装信任** |
| 登录 RSA 验签 | 放行 | 插件对签名验证做恒真补丁（服务端用自己的 RSA 私钥签名） |
| 登录口令加密 | 用私服公钥重新加密 | 插件给 `RSACryptoServiceProvider.Encrypt` 打 Postfix，换成本地公钥；服务端持对应私钥解密 |
| 网关 TCP `:30531` | 重定向 `127.0.0.1:30531` | 插件补丁 `Socket.Connect`，改写目标 IP |
| 主城 / 玩法数据 | 官方抓包回放 | 服务端加载 `Data/responses/`（73 响应 + 5 推送），按请求匹配返回 |
| 客户端资源（CDN） | 直接转发本地文件 | 服务端 CDN 控制器读客户端 `StreamingAssets`，无需联网 |

`Server/` 与 `Client-Plugin/BepInEx/plugins/UseCustomServer.dll` **配套使用**（改动服务端无需重编插件；
但插件与服务端的登录协议若变更，两者必须同时更新）。

---

## 服务端功能实现

### 掉落系统（服务端权威）

客户端**只在单机模式下**（`!isInMultiplayerBattle`）才会向服务端要掉落，联机不触发。

```
客户端 LevelEnemyDropRequest {SeqID, EnemyLootID, LevelID}
        ↓
服务端 DropTables 查表 → DropGenerator 展开随机池
        ↓
LevelEnemyDropResponse {SeqID, TurnNum, EnemyLootID, TypeDropList, BossDrop}
```

要点：

- **两套掉落体系并存，以 `EnemyDropNew` 优先**（官方服务端权威口径），`EnemyDrop` 仅作兜底
- 掉率基数为 **100000**（`200000` = 必掉两份；`40500` = 40.5%）
- 位置序号必须**从 0 连续铺满** —— 缺项会导致客户端后续同类敌人直接不掉
- 「这一只不掉」用**空消息占位**，不能被跳过
- 支持**世界池**顶替与 **BOSS 宝箱**（`_ChestCount > 0` 时写入 `BossDrop`）
- 掉落物是**地面实体**（走过去捡），不是直接进背包

### 结算奖励

结算界面的奖励来自 `SettleDataResponse` 的**两个独立字段**：

| 结算界面栏位 | 奖励类型 | 数据来源 |
|---|---|---|
| Tab[1] | StoryQuestReward | `field6 QuestUpdateInfo`（服务端下发） |
| Tab[3] | BattleDropReward（掉落奖励） | `field5 BattleUpdateInfo` |

关键语义：

- 下发的数量是**「更新后的持有总量」**，不是「本次获得量」。
  客户端处处做「新值 − 当前持有量」求差额，差额 ≤ 0 直接丢弃 → 界面空白。
- **货币必须走 `UpdateInfo.CurrencyData`**，走 `Items` 只会被当普通道具塞进背包
- **货币面值包**：`_ItemCategory == 9` 且 `Item._Amount = [N]` 表示「1 个道具 = N 个货币」，需折算
- **礼包一律拆包**：`QuestShow._Reward` 装的是礼包，展示的是内容，按 `Item._LinkID → ItemPool` 展开
- **结算请求不幂等**：客户端会对同一次结算连发多次请求，服务端需按请求字节缓存整段响应（约 1.5s），
  否则含随机项的奖励会被重复 roll，客户端把两份都入袋

### 事件模式

| 模式 | 机制 | 推进接口 |
|---|---|---|
| 悖域巡查（突击警报） | `AlertEventInfo.CurrentIndex` = 当前应打关卡的 1-based 序号，客户端只在断线重连时重发，**必须主动推送** | `gate.AlertEventPush` |
| 悖域回归（周本） | 面板只认 `MapProgressLogic:GetCurrWeeklyInfo()` 的 `ChoseQuests` / `ShowLines` | `gate.WeeklyQuestNoticePush` |

两者都靠 `SettleDataRequest` 里的关卡通关信息推进。

---

## 客户端接口对齐（热更程序集）

客户端的网络协议定义（`Protos.*` 的 Request/Response/Push，含字段编号与类型）**全部编译在热更程序集里**，
官方未公开 `.proto` 文件。因此本私服把该程序集作为接口对齐的**唯一权威来源**：

```
Server-Source/HotAssembly/Assembly-CSharp.dll   ← 客户端热更主程序集（22.5MB，仅作数据源，不参与编译）
        │
        │  tools/extract_proto_schema.py   （解析 .NET 元数据）
        ▼
Server-Source/Data/proto_schema.json      ← 430 Request / 445 Response / 69 Push 的字段级结构
        │
        │  tools/interface_gap.py
        ├──> Data/interface_gap.md                 ← 覆盖缺口报告
        └──> Generated/ClientRoutes.generated.cs   ← 路由补齐代码（自动生成）
```

**协议风格**：由 protoc 的 C# 代码生成器（Google.Protobuf）生成，**不是** protobuf-net。
字段编号存放在 `<PropertyName>FieldNumber` 常量中，值存放在首字母小写的私有后备字段 `<propertyName>_` 中。

### 当前对齐状态

| 指标 | 数量 |
|---|---|
| 客户端 Request 类型总数 | 430 |
| 由 `ClientRoutes` 自动补齐 | 741 条路由 |
| 改用真实数据响应的高频接口 | 11 |
| 结算/任务链路专用响应 | 4 |

服务端启动日志会打印：

```
ClientRoutes: 客户端接口 430 个, 本次补齐 741 个路由注册
MissingInterfaceResponses: 11 个接口已改用真实数据响应
QuestStepResponses: 4 个结算/任务链路接口
DropTables: EnemyDrop=331 EnemyDropNew=298 EnemyLoot=650 ItemPool=2843 Level=910 Item=7400 QuestShow=2500 CurrencyLink=31
```

> `Data/responses/` 下的同名 `.bin` 回放文件**优先于**真实处理器。
> 若要接管某个路由，需同时删掉 `Server/Data/responses/` 与 `Server-Source/Data/responses/` 下的同名文件。

### 游戏版本更新后如何重新对齐

```bash
pip install dnfile
cd Server-Source

# 1) 用新版客户端提取的热更程序集覆盖 HotAssembly/Assembly-CSharp.dll
# 2) 重新提取协议结构
python tools/extract_proto_schema.py HotAssembly/Assembly-CSharp.dll -o Data/proto_schema.json
# 3) 重新比对缺口并生成路由补齐代码
python tools/interface_gap.py
# 4) 重新编译
dotnet publish -c Release -o publish
```

> **注意**：`HotAssembly/` 下的 DLL 仅作为提取数据源，**请勿**放入任何会被编译器引用的目录（如 `lib/`），
> 否则会污染程序集绑定导致原生崩溃。

---

## 常见问题 FAQ

| 问题 | 处理 |
|---|---|
| 服务端启动即闪退 / 端口被占用 | 先 `taskkill /IM IntoTheVoidServer.exe /F`；确认 80/443/30531 未被占用（`netstat -ano`）；**退出 Watt Toolkit/Steam++** |
| 提示缺少 .NET | 装 .NET 10 SDK 或对应运行时（见[快速开始](#快速开始4-步)） |
| 控制台显示 `Loaded 0 captured responses` | `Data/responses` 目录缺失，重新 clone 或解压 |
| 登录提示「账号不存在」 | 必须先建号，见第 2 步 |
| 登录提示网络/账号错误 | 确认服务端 443 已启动（`curl -k https://localhost/ping` 应返回 `{"code":0}`） |
| 游戏弹「系统维护中」 | 确认插件已加载：`BepInEx/plugins/UseCustomServer.dll` 存在；查看 `BepInEx/LogOutput.log` |
| 首次进游戏卡很久 | BepInEx 正在生成 `interop/`，属正常，之后不再等 |
| 结算栏空白 / 奖励数字不对 | 多为「持有总量 vs 本次获得」语义问题，见[服务端功能实现](#服务端功能实现) |
| 掉落一堆没显示全 | 掉落走**地面实体**，需走过去捡；结算界面「掉落奖励」栏另有独立字段 |
| 换了盘符连不上 | 设置 `UCS_GAME_ROOT` 环境变量指向新游戏目录 |
| 游戏闪退（无声消失） | 查 `BepInEx/LogOutput.log`；通常是插件与服务端不配套 |
| 想联机 | 插件把服务器地址写死为 `127.0.0.1`，本包**仅支持单机**；联机需改插件常量并重编 |

---

## 免责声明

本项目仅用于**技术学习与个人研究**，请勿用于商业用途。
游戏本体及一切素材版权归原开发商所有；本仓库**不含游戏本体**，请自行准备自有客户端。
使用本私服造成的任何后果由使用者自行承担。

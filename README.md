# IntoTheVoid Private Server · 驱入虚空本地私服

> 让官方《驱入虚空》客户端跑成**完全离线本地私服**。**免改 hosts、免装证书、游戏本体零修改** —— 所有改动都在 BepInEx 插件 + 本地服务端内完成。

![state](https://img.shields.io/badge/state-可登录·可进主城·可进关卡-yellow)
![client](https://img.shields.io/badge/client-官方原版-blue)
![license](https://img.shields.io/badge/license-仅供学习研究-lightgrey)

---

## 目录

- [当前能力与已知限制](#当前能力与已知限制)
- [包内容与目录结构](#包内容与目录结构)
- [快速开始（3 步）](#快速开始3-步)
- [详细使用方法](#详细使用方法)
- [GM 管理面板](#gm-管理面板)
- [从源码构建服务端](#从源码构建服务端)
- [工作原理（免改 hosts 是怎么做到的）](#工作原理)
- [常见问题 FAQ](#常见问题-faq)
- [免责声明](#免责声明)

---

## 当前能力与已知限制

| 项 | 状态 |
|---|---|
| 免改 hosts 登录 | ✅ 插件内存级拦截，不动系统 hosts |
| 登录 / 进主城 | ✅ 实测通过 |
| 进关卡（战斗/副本） | ✅ 实测通过 |
| 关卡内刷新敌对单位 | ❌ **尚未实现**（服务端暂不回放/生成刷怪数据，进入后场景为空） |

> ⚠️ 刷怪逻辑属于服务端主动行为（AI/波次推送），现有回放数据里不含该链路，需要后续按官方抓包补「刷怪/波次请求响应」或自行实现生成逻辑。本仓库当前能稳定跑到「进入关卡」。

---

## 包内容与目录结构

```
IntoTheVoidPrivateServer/
├── README.md                    # 本文件（使用方法）
├── .gitignore
├── Server/                      # 服务端【发布版，开箱即用】
│   ├── IntoTheVoidServer.exe    # 主程序（双击即运行）
│   ├── Data/responses/          # 官方抓包回放数据（74 响应 + 5 推送，必备）
│   ├── wwwroot/admin/           # GM 管理面板页面
│   ├── cert.pfx / cert.cer      # HTTPS 自签证书（SAN 覆盖官方域名）
│   └── rsa_private_key.txt      # 服务端登录签名 RSA 私钥
├── Server-Source/               # 服务端【源码版，可二次开发】
│   ├── IntoTheVoidServer.csproj # (编译需 .NET 10 SDK)
│   ├── Program.cs / *.cs / Http/ / Net/ / Pomelo/ / Router/
│   └── Data/ wwwroot/ 证书/密钥
├── Client-Plugin/               # 客户端【BepInEx 插件包，免改 hosts 的核心】
│   ├── BepInEx/                 # core + plugins(UseCustomServer) + config + patchers
│   ├── dotnet/                  # IL2CPP 所需 .NET 运行时（已自包含）
│   ├── winhttp.dll              # BepInEx 注入器
│   └── doorstop_config.ini / .doorstop_version
└── docs/                        # 一键安装脚本（可选）
```

> 本包**不含游戏本体**（`IntoTheVoid.exe` / `GameAssembly.dll` / `UnityPlayer.dll` / 游戏资源等）。你需要一套自己的官方客户端。

---

## 快速开始（3 步）

### 前提
- 一套**干净的官方成品客户端**（含 `IntoTheVoid.exe`）
- 若私服服务端起不来：安装 [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

### 第 1 步：把客户端插件合并进游戏目录
把 `Client-Plugin/` 里的**全部文件**拷贝到游戏根目录（与 `IntoTheVoid.exe` 同级），合并：

```
游戏根目录/
├── IntoTheVoid.exe          (官方原有)
├── IntoTheVoid_Data/        (官方原有)
├── BepInEx/                 ← 拷贝
├── dotnet/                  ← 拷贝
├── winhttp.dll              ← 拷贝
├── doorstop_config.ini      ← 拷贝
└── .doorstop_version        ← 拷贝
```

> BepInEx 首次启动会自动生成 `BepInEx/interop/` 与 `BepInEx/unity-libs/`（无需手动处理）。

### 第 2 步：启动私服服务端
双击 `Server/IntoTheVoidServer.exe`。
看到控制台首屏日志出现以下内容即为正常：

```
Loaded 74 captured responses and 5 pushes   ← 必须有，0 则说明 Data/responses 缺失
Now listening on: http://0.0.0.0:80 / 443 / 8183 ...
```

> 服务端监听 **HTTP 80 / HTTPS 443 / 8183(管理) / TCP 30531(网关)**。
> 也提供一键脚本：右键 `docs/一键安装.bat` → 以管理员身份运行（会自动探测游戏目录并启动服务端）。

### 第 3 步：启动游戏
双击 `IntoTheVoid.exe`，登录任意账号密码即可（本地私服不校验真实账号）。

---

## 详细使用方法

### 验证是否成功
- 登录后应自动进入主城；可打开**关卡界面进入战斗关卡**。
- 服务端控制台会持续打印客户端的 HTTP/TCP 请求与回放命中记录。
- GM 面板：浏览器打开 <http://127.0.0.1:8183/admin/>

### 更换磁盘 / 游戏目录移动后
服务端会自动向上探测 `IntoTheVoid.exe` 定位游戏根目录；探测不到时设置环境变量：

```
UCS_GAME_ROOT=D:\你的游戏目录
```

### 服务端日志
- 发布版：`Server/logs/server_YYYYMMDD.log`
- 源码运行版：`IntoTheVoidServer/logs/`

---

## GM 管理面板

浏览器打开 **<http://127.0.0.1:8183/admin/>**

提供两类功能按钮（页面自带说明）：
- **常用 GM 操作**：若干预置 GM 指令
- **货币发放**：选择货币类型（晶卷 CrystalCredit=21 / 金币 / 垄金 / 绑定晶卷），输入数量发放

> ⚠️ 货币等状态保存在服务端**内存**（GameState），服务端重启后归零，需重新发放。可编辑 `Server/Data/gamestate.json` 预设初始值。

---

## 从源码构建服务端

```bash
# 需要 .NET 10 SDK
cd Server-Source
dotnet restore
dotnet publish -c Release -o publish
# 产物需自行带上证书与 Data（csproj 已配置 Data/responses 自动复制）
```

---

## 工作原理

免改 hosts、免装证书由 **BepInEx 插件（UseCustomServer）** 在进程内完成，hosts 文件完全不用动：

| 官方行为 | 本地处理 | 手段 |
|---|---|---|
| HTTP(S) 请求 `*.jinzhangshu.com` | 解析回 `127.0.0.1` | 插件 Hook `Dns.GetHostAddresses`，6 个官方域一律返回本地回环地址（**登录链 BestHTTP 实测走此方法**） |
| HTTPS 443 | 本地 Kestrel 自签证书应答 | 证书 SAN 已覆盖全部官方域；客户端 BestHTTP 默认放行自签证书，**无需安装信任** |
| 登录 RSA 验签 | 放行 | 插件对签名验证做恒真补丁（服务端用自己的 RSA 私钥签名） |
| 网关 TCP `1.13.127.58:30531` | 重定向 `127.0.0.1:30531` | 插件补丁 `Socket.Connect`，具名参数改写目标 IP |
| 主城/玩法数据 | 官方抓包回放 | 服务端加载 `Data/responses/`（74 响应 + 5 推送），按请求匹配返回 |

`Server/` 与 `Client-Plugin/BepInEx/plugins/UseCustomServer.dll` 配套使用（版本要一致）。

---

## 常见问题 FAQ

| 问题 | 处理 |
|---|---|
| 服务端启动即闪退 / 端口被占用 | 先 `taskkill /IM IntoTheVoidServer.exe /F`；确认 80/443/30531 未被其他程序占用（`netstat -ano`） |
| 控制台显示 `Loaded 0 captured responses` | `Data/responses` 目录缺失或被删，重新解压本包 |
| 游戏弹「系统维护中」 | 确认 BepInEx 插件已加载：游戏目录下 `BepInEx/plugins/UseCustomServer.dll` 存在；查看 `BepInEx/LogOutput.log` 有无 UseCustomServer 日志 |
| 登录提示网络/账号错误 | 确认服务端 443 已启动（`curl -k https://localhost/ping` 应返回 `{"code":0}`） |
| 进入关卡后没有敌人 | 已知限制，见上表；当前服务端未实现刷怪链路 |
| 换了盘符连不上 | 设置 `UCS_GAME_ROOT` 环境变量指向新游戏目录 |
| 游戏闪退（无声消失） | 查 `BepInEx/LogOutput.log`；通常是插件版本与服务端不配套，或热更补丁开关 `UCS_PATCH_DLL` 被误设 |

---

## 免责声明

本项目仅用于**技术学习与个人研究**，请勿用于商业用途。游戏本体及一切素材版权归原开发商所有；本仓库不含游戏本体，请自行准备正版/自有客户端。使用本私服造成的任何后果由使用者自行承担。

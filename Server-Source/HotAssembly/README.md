# HotAssembly · 客户端热更程序集

本目录存放驱入虚空客户端的 **HybridCLR 热更主程序集**，是本地私服做「接口对齐」的
**唯一权威来源 (source of truth)**。

## 为什么需要它

游戏的网络协议定义（`Protos.*` 的 Request / Response / Push 消息，含字段编号与类型）
全部编译在这份程序集里，官方未公开任何 `.proto` 文件。私服要想与客户端接口严格对齐，
只能从这份程序集中提取协议结构。

## 文件

| 文件 | 说明 |
|---|---|
| `Assembly-CSharp.dll` | 热更主程序集（22.5MB）。含 `TPS.RunTime` 战斗框架、`Protos.*` 协议定义 |
| `来源说明.txt` | 提取来源与哈希校验记录 |

> `Assembly-CSharp-firstpass.dll`（Ink / Spine / BestHTTP 等第三方库）不包含协议定义，
> 未一并归档；如需完整反编译可从提取目录补充。

## 协议风格

由 **protoc 的 C# 代码生成器（Google.Protobuf）** 生成，而非 protobuf-net。特征：

- 消息类含 `_parser` / `_unknownFields` 成员
- 字段编号存放在 `<PropertyName>FieldNumber` 常量中
- 真实值存放在首字母小写的私有后备字段 `<propertyName>_` 中

提取脚本据此还原字段编号与类型。

## 提取协议结构

```bash
# 安装依赖（仅需一次）
pip install dnfile

# 从程序集提取全量协议 Schema -> Data/proto_schema.json
python tools/extract_proto_schema.py HotAssembly/Assembly-CSharp.dll -o Data/proto_schema.json

# 比对客户端接口全集与私服覆盖缺口，并生成路由补齐代码
python tools/interface_gap.py
```

产物：

| 产物 | 说明 |
|---|---|
| `Data/proto_schema.json` | 全部 `Protos.*` 消息字段级结构 |
| `Data/interface_gap.md` | 覆盖缺口报告（客户端接口 vs 私服已覆盖） |
| `Generated/ClientRoutes.generated.cs` | 路由补齐代码（自动生成，勿手改） |

## 更新程序集后

游戏版本更新 → 重新提取客户端热更程序集 → 覆盖本目录的 `Assembly-CSharp.dll`
→ 重跑上面两条命令 → 重新编译服务端。缺口报告会直接告诉你新版本新增/变更了哪些接口。

## 注意

该 DLL **不参与服务端编译**，仅作为提取数据源。请勿将其放入任何会被编译器引用的
目录（如 `lib/`），否则会污染程序集绑定。

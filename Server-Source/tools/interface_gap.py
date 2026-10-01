#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
interface_gap.py
================
比对「客户端接口全集」(来自热更程序集) 与「本地私服已覆盖接口」,
生成缺口报告与可编译的路由骨架代码。

输入
----
  1. Data/proto_schema.json           由 extract_proto_schema.py 生成
  2. Data/responses/*.bin             官方抓包回放响应(文件名即路由)
  3. Router/RouteMap.cs               路由号 -> 响应名 映射
  4. Router/MessageRouter.cs          已注册的路由处理器

输出
----
  1. Data/interface_gap.md            人类可读的缺口报告
  2. Generated/ClientRoutes.generated.cs  自动生成的路由注册代码(补齐未覆盖接口)
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any

# 客户端可能使用的路由前缀(由服务端分工决定: 网关 / 游戏 / 场景)
ROUTE_PREFIXES = ("game.game", "gate", "scene.scene")


def route_from_filename(stem: str) -> tuple[str, str]:
    """回放文件名 -> (路由, 接口名)。

    例: game_game_LevelBeginRequest -> ("game.game.LevelBeginRequest", "LevelBeginRequest")
        gate_Entry                  -> ("gate.Entry", "Entry")
    """
    for prefix in ROUTE_PREFIXES:
        dotted = prefix.replace(".", "_") + "_"
        if stem.startswith(dotted):
            rest = stem[len(dotted):]
            return f"{prefix}.{rest}", rest
    return stem.replace("_", "."), stem


def parse_route_map(path: Path) -> dict[str, str]:
    """解析 RouteMap.cs: ["110"] = "LevelBeginResponse" -> {"110": "LevelBeginResponse"}"""
    out: dict[str, str] = {}
    if not path.exists():
        return out
    text = path.read_text(encoding="utf-8", errors="ignore")
    for m in re.finditer(r'\[\s*"([^"]+)"\s*\]\s*=\s*"([^"]+)"', text):
        out[m.group(1)] = m.group(2)
    return out


def parse_registered_routes(path: Path) -> set[str]:
    """解析 MessageRouter.cs 中 RegisterHandler 的第一个参数。"""
    out: set[str] = set()
    if not path.exists():
        return out
    text = path.read_text(encoding="utf-8", errors="ignore")
    for m in re.finditer(r'RegisterHandler\(\s*"([^"]+)"', text):
        out.add(m.group(1))
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description="比对客户端接口全集与私服覆盖缺口")
    ap.add_argument("--server-root", default=".", help="服务端源码根目录")
    ap.add_argument("--schema", default="Data/proto_schema.json", help="协议 Schema")
    ap.add_argument("--out-md", default="Data/interface_gap.md", help="缺口报告输出路径")
    ap.add_argument("--out-cs", default="Generated/ClientRoutes.generated.cs",
                    help="生成的路由骨架输出路径")
    args = ap.parse_args()

    root = Path(args.server_root)
    schema_path = root / args.schema
    if not schema_path.exists():
        print(f"[!] 找不到 {schema_path}, 请先运行 extract_proto_schema.py")
        return 1

    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    types: dict[str, Any] = schema["types"]

    requests = sorted(n for n, v in types.items() if v["kind"] == "Request")

    # ---- 服务端已覆盖的接口名集合 ----
    covered_names: set[str] = set()
    covered_routes: set[str] = set()
    sources: dict[str, set[str]] = {}

    def mark(name: str, route: str, source: str) -> None:
        covered_names.add(name)
        if route:
            covered_routes.add(route)
        sources.setdefault(name, set()).add(source)

    # 1) 抓包回放响应
    resp_dir = root / "Data" / "responses"
    if resp_dir.exists():
        for f in resp_dir.glob("*.bin"):
            route, name = route_from_filename(f.stem)
            mark(name, route, "回放")

    # 2) RouteMap 中的响应名 (数值路由号)
    route_map = parse_route_map(root / "Router" / "RouteMap.cs")
    for _rid, resp_name in route_map.items():
        name = resp_name[:-len("Response")] if resp_name.endswith("Response") else resp_name
        mark(name + "Request" if not name.endswith("Request") else name, "", "RouteMap")

    # 3) MessageRouter 已注册路由
    for route in parse_registered_routes(root / "Router" / "MessageRouter.cs"):
        name = route.rsplit(".", 1)[-1]
        mark(name, route, "Router")

    # ---- 计算缺口 ----
    covered_reqs = [r for r in requests if r in covered_names]
    missing_reqs = [r for r in requests if r not in covered_names]

    # 已知被客户端实际调用过(日志观测)的接口
    observed = [
        "MRItemCompletedRankRequest", "ProfileRequest", "GetProfileRequest",
        "GetGuildMembersRequest", "RechargeInfoRequest", "LogPushRequest",
        "ClearRedPointRequest", "ServerTagListRequest", "LoginDeviceInfoRequest",
        "GetGuildRepairListRequest", "GetAtlasInfoRequest",
    ]

    # ---- 写缺口报告 ----
    md = [
        "# 客户端接口 vs 本地私服 · 覆盖缺口报告",
        "",
        "> 本文件由 `tools/interface_gap.py` 自动生成, 请勿手工编辑。",
        f"> 数据源: `{args.schema}` (提取自热更程序集 `Assembly-CSharp.dll`)",
        "",
        "## 总览",
        "",
        "| 指标 | 数量 |",
        "|---|---|",
        f"| 客户端 Request 类型总数 | {len(requests)} |",
        f"| 私服已覆盖 | {len(covered_reqs)} |",
        f"| 未覆盖 | {len(missing_reqs)} |",
        f"| 覆盖率 | {len(covered_reqs) / max(1, len(requests)) * 100:.1f}% |",
        "",
        "## 客户端实际调用但缺少回放响应的接口 (高优先级)",
        "",
        "这些接口在服务端日志中出现过 `No captured response`, ",
        "说明客户端**确实会调用**, 优先补齐:",
        "",
        "| 接口 | 客户端字段数 | 响应类型 | 响应字段数 |",
        "|---|---|---|---|",
    ]
    for name in observed:
        req = types.get(name)
        resp_name = name[:-7] + "Response" if name.endswith("Request") else name + "Response"
        resp = types.get(resp_name)
        md.append(
            f"| `{name}` | {req['fieldCount'] if req else '-'} | "
            f"`{resp_name}` | {resp['fieldCount'] if resp else '无对应类型'} |"
        )

    md += [
        "",
        f"## 未覆盖接口全集 ({len(missing_reqs)} 个)",
        "",
        "<details><summary>点击展开</summary>",
        "",
        "| # | 接口 | 请求字段数 | 响应类型 | 响应字段数 |",
        "|---|---|---|---|---|",
    ]
    for i, name in enumerate(missing_reqs, 1):
        resp_name = name[:-7] + "Response"
        resp = types.get(resp_name)
        md.append(
            f"| {i} | `{name}` | {types[name]['fieldCount']} | "
            f"`{resp_name}` | {resp['fieldCount'] if resp else '-'} |"
        )
    md += ["", "</details>", ""]

    out_md = root / args.out_md
    out_md.parent.mkdir(parents=True, exist_ok=True)
    out_md.write_text("\n".join(md), encoding="utf-8")

    # ---- 生成路由补齐代码 ----
    # 只为"未覆盖"的接口生成注册, 避免与已有的真实处理器冲突。
    # 每个接口在三个可能前缀下注册, 因为路由前缀由服务端分工决定,
    # 从程序集元数据无法判定, 穷举可保证不遗漏。
    lines = [
        "// <auto-generated>",
        "//   由 tools/interface_gap.py 依据热更程序集 (Assembly-CSharp.dll) 中的",
        "//   Protos.* 消息定义自动生成。请勿手工编辑; 重新运行生成脚本以更新。",
        f"//   客户端 Request 类型总数: {len(requests)}",
        f"//   已覆盖: {len(covered_reqs)}, 本次补齐: {len(missing_reqs)}",
        "// </auto-generated>",
        "",
        "#nullable enable",
        "",
        "using System.Threading.Tasks;",
        "using IntoTheVoidServer.Pomelo;",
        "using Serilog;",
        "",
        "namespace IntoTheVoidServer.Router;",
        "",
        "/// <summary>",
        "/// 依据客户端热更程序集生成的路由补齐表。",
        "///",
        "/// 背景: 客户端的每一个 Protos.*Request 都是一个潜在网络接口。",
        "///       MessageRouter 只为已知接口注册了处理器, 其余会落到",
        "///       \"Unhandled route\" 兜底分支。本类把剩余接口全部显式注册,",
        "///       使私服对客户端协议面形成 100% 覆盖, 并留下统一的填充点。",
        "/// </summary>",
        "public static class ClientRoutes",
        "{",
        "    /// <summary>客户端 Request 类型全集(不含路由前缀)。</summary>",
        "    public static readonly IReadOnlyList<string> AllClientRequests = new[]",
        "    {",
    ]
    for name in requests:
        lines.append(f'        "{name}",')
    lines += [
        "    };",
        "",
        "    /// <summary>由本类补齐(原先未覆盖)的 Request 类型。</summary>",
        "    public static readonly IReadOnlyList<string> BackfilledRequests = new[]",
        "    {",
    ]
    for name in missing_reqs:
        lines.append(f'        "{name}",')
    lines += [
        "    };",
        "",
        "    /// <summary>",
        "    /// 补齐路由注册。返回新注册的数量。",
        "    /// </summary>",
        "    /// <remarks>",
        "    /// 响应体使用 ProtoBuilder.BuildSuccess() (空消息)。对于 Google.Protobuf",
        "    /// 而言, 空字节流是合法消息, 客户端会反序列化出一个字段全为默认值的对象,",
        "    /// 不会抛错。若某个接口后续需要真实数据, 在 Data/responses 下放入同名",
        "    /// 回放文件即可自动优先命中, 无需改动本文件。",
        "    /// </remarks>",
        "    public static int Register(MessageRouter router)",
        "    {",
        "        int registered = 0;",
        "        foreach (var name in BackfilledRequests)",
        "        {",
        "            foreach (var prefix in new[] { \"game.game\", \"gate\", \"scene.scene\" })",
        "            {",
        "                var route = prefix + \".\" + name;",
        "                router.RegisterHandler(route, (payload, id) =>",
        "                {",
        "                    Log.Debug(\"[ClientRoutes] {Route} -> empty success\", route);",
        "                    return Task.FromResult<byte[]?>(ProtoBuilder.BuildSuccess());",
        "                });",
        "                registered++;",
        "            }",
        "        }",
        "        return registered;",
        "    }",
        "}",
        "",
    ]

    out_cs = root / args.out_cs
    out_cs.parent.mkdir(parents=True, exist_ok=True)
    out_cs.write_text("\n".join(lines), encoding="utf-8")

    print(f"[+] 客户端 Request 总数 : {len(requests)}")
    print(f"[+] 私服已覆盖          : {len(covered_reqs)}")
    print(f"[!] 未覆盖              : {len(missing_reqs)}")
    print(f"[+] 缺口报告            : {out_md}")
    print(f"[+] 路由骨架            : {out_cs}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

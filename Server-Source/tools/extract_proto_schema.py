#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
extract_proto_schema.py
=======================
从驱入虚空热更程序集 (Assembly-CSharp.dll) 中提取全部网络协议的
Request / Response 类型定义(含 protobuf 字段编号与类型), 输出 JSON Schema。

协议风格说明
------------
本游戏的 Proto 由 protoc 的 C# 代码生成器 (Google.Protobuf) 生成, 而非
protobuf-net。其特征为:
  - 每个消息类含 `_parser` / `_unknownFields` 成员
  - 字段编号存放在 `<PropertyName>FieldNumber` 常量字段中(Constant 表)
  - 真实值存放在首字母小写的私有后备字段 `<propertyName>_` 中
因此本脚本:
  1. 从 Constant 表读取 `*FieldNumber` 常量的值 -> 字段编号
  2. 由常量名推导属性名 -> 推导后备字段名 -> 解析字段签名得到类型

产出物用于
----------
  1. 生成本地私服可直接使用的 DTO (Protos.Generated.cs)
  2. 生成本地私服路由骨架 (ClientRoutes.generated.cs)
  3. 比对"客户端接口全集"与"私服已覆盖接口"的缺口 (interface_gap.md)

用法
----
    python extract_proto_schema.py <Assembly-CSharp.dll> [-o proto_schema.json]

依赖
----
    pip install dnfile
"""
from __future__ import annotations

import argparse
import json
import sys
from typing import Any

try:
    import dnfile
    from dnfile.enums import MetadataTables
except ImportError:
    sys.exit("缺少依赖, 请先执行: pip install dnfile")


PROTOS_NAMESPACE = "Protos"
FIELD_TABLE = int(MetadataTables.Field)
TYPEDEF_TABLE = int(MetadataTables.TypeDef)

# ECMA-335 II.23.1.16 元素类型
ELEMENT_TYPE_NAMES = {
    0x02: "bool",
    0x03: "char",
    0x04: "sbyte",
    0x05: "byte",
    0x06: "short",
    0x07: "ushort",
    0x08: "int",
    0x09: "uint",
    0x0A: "long",
    0x0B: "ulong",
    0x0C: "float",
    0x0D: "double",
    0x0E: "string",
    0x11: "byte[]",
    0x1C: "object",
    0x0F: "pointer",
    0x10: "byref",
    0x13: "var",
    0x1E: "mdtvar",
}


# ---------------------------------------------------------------------------
# 常量表索引: (表号, 行号) -> 常量值
# ---------------------------------------------------------------------------
def build_constant_index(pe) -> dict[tuple[int, int], Any]:
    idx: dict[tuple[int, int], Any] = {}
    const_table = getattr(pe.net.mdtables, "Constant", None)
    if const_table is None:
        return idx
    for row in const_table.rows:
        try:
            tnum = row.Parent.table.number
            rid = row.Parent.row_index
            raw = getattr(row.Value, "value", row.Value)
        except Exception:
            continue
        if raw is None:
            continue
        raw = bytes(raw) if not isinstance(raw, bytes) else raw
        # 按 Constant.Type 解释: 0x08=int32, 0x09=uint32, 0x0A=int64 ...
        try:
            ctype = row.Type
        except Exception:
            ctype = None
        try:
            if ctype == 0x08:      # ELEMENT_TYPE_I4
                val: Any = int.from_bytes(raw, "little", signed=True)
            elif ctype == 0x09:    # U4
                val = int.from_bytes(raw, "little", signed=False)
            elif ctype == 0x0A:    # I8
                val = int.from_bytes(raw, "little", signed=True)
            elif ctype == 0x0E:    # STRING (UTF-16LE)
                val = raw.decode("utf-16-le", "ignore")
            else:
                val = int.from_bytes(raw, "little", signed=True)
        except Exception:
            val = None
        idx[(tnum, rid)] = val
    return idx


# ---------------------------------------------------------------------------
# 字段签名解码
# ---------------------------------------------------------------------------
def read_compressed_uint(sig: bytes, i: int) -> tuple[int, int]:
    """解码 ECMA-335 II.23.2 压缩无符号整数, 返回 (值, 新偏移)。"""
    if i >= len(sig):
        return 0, i
    b0 = sig[i]
    if b0 & 0x80 == 0:            # 1 字节
        return b0, i + 1
    if b0 & 0xC0 == 0x80:         # 2 字节
        if i + 1 >= len(sig):
            return 0, i + 1
        return ((b0 & 0x3F) << 8) | sig[i + 1], i + 2
    if b0 & 0xE0 == 0xC0:         # 4 字节
        if i + 3 >= len(sig):
            return 0, i + 4
        return (((b0 & 0x1F) << 24) | (sig[i + 1] << 16)
                | (sig[i + 2] << 8) | sig[i + 3]), i + 4
    return b0, i + 1


def decode_type(sig: bytes, i: int, type_names: dict[int, str],
                typeref_names: dict[int, str] | None = None) -> tuple[int, str]:
    """解码签名的单个 Type, 返回 (新偏移, 类型名)。"""
    typeref_names = typeref_names or {}
    et = sig[i]
    i += 1

    if et in ELEMENT_TYPE_NAMES:
        return i, ELEMENT_TYPE_NAMES[et]

    if et == 0x1D:  # SZARRAY
        i, inner = decode_type(sig, i, type_names, typeref_names)
        return i, f"{inner}[]"

    if et == 0x15:  # GENERICINST
        i, base = decode_type(sig, i, type_names, typeref_names)
        if i >= len(sig):
            return i, base
        cnt = sig[i]
        i += 1
        args = []
        for _ in range(cnt):
            if i >= len(sig):
                break
            i, a = decode_type(sig, i, type_names, typeref_names)
            args.append(a)
        return i, f"{base}<{','.join(args)}>" if args else base

    if et in (0x11, 0x12):  # CLASS / VALUETYPE -> TypeDefOrRef 压缩编码索引
        coded, i = read_compressed_uint(sig, i)
        tag, rid = coded & 0x03, coded >> 2
        if tag == 0:  # TypeDef
            return i, type_names.get(rid, f"TypeDef#{rid}")
        if tag == 1:  # TypeRef
            return i, typeref_names.get(rid, f"TypeRef#{rid}")
        return i, f"TypeSpec#{rid}"

    if et in (0x1F, 0x20):  # CMOD_REQD / CMOD_OPT
        _coded, i = read_compressed_uint(sig, i)
        return decode_type(sig, i, type_names, typeref_names)

    if et == 0x14:  # ARRAY
        i, inner = decode_type(sig, i, type_names)
        return i, f"{inner}[]"

    if et == 0x10:  # BYREF
        i, inner = decode_type(sig, i, type_names)
        return i, f"{inner}&"

    if et == 0x0F:  # PTR
        i, inner = decode_type(sig, i, type_names)
        return i, f"{inner}*"

    return i, f"et_0x{et:02X}"


def decode_field_signature(sig: bytes, type_names: dict[int, str],
                           typeref_names: dict[int, str] | None = None) -> str:
    """FieldSig ::= 0x06 (FIELD) <Type>"""
    if not sig or sig[0] != 0x06:
        return "unknown"
    try:
        return decode_type(sig, 1, type_names, typeref_names)[1]
    except Exception:
        return "unknown"


# ---------------------------------------------------------------------------
# Google.Protobuf 命名约定: 属性名 -> 后备字段名
# ---------------------------------------------------------------------------
def backing_field_name(prop: str) -> str:
    """protoc C# 生成规则: 属性 `UID` 的后备字段为 `uID_`。

    对于首字符已为小写的属性名(如 `value`), 后备字段为 `value_`。
    """
    if not prop:
        return prop
    lowered = prop[0].lower() + prop[1:]
    return lowered + "_"


def normalize_repeated(t: str) -> tuple[str, bool]:
    """识别 RepeatedField<T> / MapField<K,V>, 返回 (元素类型, 是否重复)。"""
    if t.startswith("RepeatedField<") and t.endswith(">"):
        return t[len("RepeatedField<"):-1], True
    if t.startswith("MapField<") and t.endswith(">"):
        return t[len("MapField<"):-1], True
    return t, False


# ---------------------------------------------------------------------------
# 主提取逻辑
# ---------------------------------------------------------------------------
def extract(pe, const_index: dict[tuple[int, int], Any],
            include_all: bool = False) -> dict[str, Any]:
    """提取 Protos 命名空间下的 protobuf 消息定义。

    include_all=False (默认): 仅提取 *Request / *Response / *Push / *Notify,
        用于接口清单与路由补齐。
    include_all=True: 额外提取所有含 FieldNumber 常量的消息体
        (如 SingleRoomResource / EnemyDropInfo / BoostType 等嵌套 DTO),
        这些类型没有 Request/Response 后缀, 却是关卡刷怪等核心数据载体。
    """
    type_defs = pe.net.mdtables.TypeDef.rows

    type_names: dict[int, str] = {}
    for rid, trow in enumerate(type_defs, start=1):
        ns = str(trow.TypeNamespace or "")
        nm = str(trow.TypeName or "")
        type_names[rid] = f"{ns}.{nm}" if ns else nm

    # TypeRef 名称索引: 外部程序集(如 Google.Protobuf)的类型引用
    typeref_names: dict[int, str] = {}
    typeref_table = getattr(pe.net.mdtables, "TypeRef", None)
    if typeref_table is not None:
        for rid, rrow in enumerate(typeref_table.rows, start=1):
            ns = str(rrow.TypeNamespace or "")
            nm = str(rrow.TypeName or "")
            typeref_names[rid] = f"{ns}.{nm}" if ns else nm

    results: dict[str, Any] = {}

    for rid, trow in enumerate(type_defs, start=1):
        ns = str(trow.TypeNamespace or "")
        name = str(trow.TypeName or "")
        if ns != PROTOS_NAMESPACE:
            continue
        is_endpoint = (name.endswith("Request") or name.endswith("Response")
                       or name.endswith("Push") or name.endswith("Notify"))
        if not is_endpoint and not include_all:
            continue

        # --- 第一遍: 收集 字段编号常量 与 后备字段签名 ---
        field_numbers: dict[str, int] = {}
        backing_sigs: dict[str, bytes] = {}

        for f in trow.FieldList:
            # FieldList 的元素是 MDTableIndex: .row 为 Field 行, .row_index 为行号
            frow = f.row if hasattr(f, "row") else f
            frid = getattr(f, "row_index", None)
            if frid is None:
                frid = getattr(frow, "row_index", None)
            fname = str(frow.Name or "")
            if not fname:
                continue

            if fname.endswith("FieldNumber"):
                num = const_index.get((FIELD_TABLE, frid)) if frid is not None else None
                if isinstance(num, int):
                    field_numbers[fname[: -len("FieldNumber")]] = num
                continue

            if fname.endswith("_"):
                raw = getattr(frow.Signature, "value", frow.Signature)
                if raw is not None:
                    backing_sigs[fname] = bytes(raw)

        # 非 endpoint 且没有 FieldNumber 常量 -> 不是 protobuf 消息, 跳过
        if not field_numbers and not is_endpoint:
            continue

        # --- 第二遍: 组装字段列表 ---
        fields: list[dict[str, Any]] = []
        for prop, num in field_numbers.items():
            bf = backing_field_name(prop)
            raw_type = "unknown"
            if bf in backing_sigs:
                raw_type = decode_field_signature(
                    backing_sigs[bf], type_names, typeref_names
                )
            elem_type, repeated = normalize_repeated(raw_type)
            fields.append({
                "number": num,
                "name": prop,
                "type": elem_type,
                "repeated": repeated,
                "rawType": raw_type,
            })

        fields.sort(key=lambda f: f["number"])

        # 嵌套消息引用(用于依赖排序与文档)
        refs = sorted({
            f["type"].split(".")[-1]
            for f in fields
            if f["type"].startswith("Protos.")
        })

        if name.endswith("Request"):
            kind = "Request"
        elif name.endswith("Response"):
            kind = "Response"
        elif name.endswith("Push") or name.endswith("Notify"):
            kind = "Push"
        else:
            kind = "Message"

        results[name] = {
            "namespace": ns,
            "kind": kind,
            "fieldCount": len(fields),
            "fields": fields,
            "messageRefs": refs,
        }

    return results


def main() -> int:
    ap = argparse.ArgumentParser(
        description="从驱入虚空热更程序集提取网络协议 Schema (Google.Protobuf 风格)"
    )
    ap.add_argument("dll", help="Assembly-CSharp.dll 路径")
    ap.add_argument("-o", "--output", default="proto_schema.json",
                    help="输出 JSON 路径 (默认 proto_schema.json)")
    ap.add_argument("--all", dest="include_all", action="store_true",
                    help="额外提取所有 Protobuf 消息体(无 Request/Response 后缀的嵌套 DTO)")
    args = ap.parse_args()

    if args.include_all and args.output == "proto_schema.json":
        args.output = "proto_schema_full.json"

    print(f"[*] 载入程序集: {args.dll}", flush=True)
    pe = dnfile.dnPE(args.dll)

    print("[*] 建立常量索引...", flush=True)
    const_index = build_constant_index(pe)
    print(f"    -> {len(const_index)} 条常量", flush=True)

    mode = "全部 Protobuf 消息" if args.include_all else "仅 Request/Response/Push"
    print(f"[*] 提取 Protos.* 消息定义 ({mode})...", flush=True)
    schema = extract(pe, const_index, include_all=args.include_all)

    reqs = [k for k, v in schema.items() if v["kind"] == "Request"]
    resps = [k for k, v in schema.items() if v["kind"] == "Response"]
    pushes = [k for k, v in schema.items() if v["kind"] == "Push"]
    msgs = [k for k, v in schema.items() if v["kind"] == "Message"]
    paired = sum(1 for r in reqs if r[:-7] + "Response" in schema)
    empty = [k for k, v in schema.items() if v["fieldCount"] == 0]

    stats = {
        "requestTypes": len(reqs),
        "responseTypes": len(resps),
        "pushTypes": len(pushes),
        "pairedRequestResponse": paired,
        "typesWithoutFields": len(empty),
    }
    if args.include_all:
        stats["messageTypes"] = len(msgs)

    out = {
        "source": {
            "assembly": args.dll,
            "extractor": "extract_proto_schema.py",
            "namespace": PROTOS_NAMESPACE,
            "protobufFlavor": "Google.Protobuf (protoc C# codegen)",
            "mode": mode,
        },
        "stats": stats,
        "types": schema,
    }

    with open(args.output, "w", encoding="utf-8") as fp:
        json.dump(out, fp, ensure_ascii=False, indent=2)

    print()
    print(f"[+] Request  类型             : {len(reqs)}")
    print(f"[+] Response 类型             : {len(resps)}")
    print(f"[+] Push     类型             : {len(pushes)}")
    if args.include_all:
        print(f"[+] Message  类型 (嵌套 DTO)  : {len(msgs)}")
    print(f"[+] 可配对 Request->Response  : {paired}")
    print(f"[!] 无字段的类型              : {len(empty)}")
    print(f"[+] 已写出: {args.output}")

    # 抽样展示
    for sample in ("GetProfileResponse", "ServerTagListResponse"):
        if sample in schema:
            print(f"\n--- 样例 {sample} ({schema[sample]['fieldCount']} 字段) ---")
            for f in schema[sample]["fields"][:10]:
                rep = " repeated" if f["repeated"] else ""
                print(f"    #{f['number']:<3} {f['name']:<24} {f['type']}{rep}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
probe_level.py
==============
直连本地 Pomelo TCP 服务 (127.0.0.1:30531), 模拟客户端发起
LevelBegin / LevelResource 请求, 验证服务端是否正确回显请求的 LevelID。

背景
----
官方抓包回放里 LevelBeginResponse / LevelResourceResponse 硬编码了主城
关卡 ID 43400330。客户端 LevelManager.RecLevelBeginRes 会校验
response.LevelID == 请求的 levelID, 不等则不推进关卡初始化 -> 不刷怪。
服务端已加入 ProtoLevelIdRewriter 做回显, 本脚本用于验证该逻辑。

用法
----
    python probe_level.py [LevelID ...]

缺省会测试: 43400001(1-1_铜釜-剧情1) 43400011(1-1_普通) 43400330(主城)
"""
from __future__ import annotations

import socket
import sys

HOST, PORT = "127.0.0.1", 30531

PKT_HANDSHAKE = 0x01
PKT_HANDSHAKE_ACK = 0x02
PKT_HEARTBEAT = 0x03
PKT_DATA = 0x04

MSG_REQUEST = 0
MSG_RESPONSE = 2

HANDSHAKE_JSON = (
    '{"sys":{"version":"0.3.0","type":"ws-client",'
    '"pomelo":"pomelo-jsclient-websocket","user":{}}}'
)


# ---------------------------------------------------------------------------
def encode_varint(value: int) -> bytes:
    out = bytearray()
    v = value & 0xFFFFFFFFFFFFFFFF
    while v >= 0x80:
        out.append((v & 0x7F) | 0x80)
        v >>= 7
    out.append(v)
    return bytes(out)


def decode_varint(data: bytes, offset: int) -> tuple[int, int]:
    result = 0
    shift = 0
    i = offset
    while i < len(data):
        b = data[i]
        i += 1
        result |= (b & 0x7F) << shift
        if not b & 0x80:
            return result, i
        shift += 7
    raise ValueError("varint 截断")


def make_packet(packet_type: int, payload: bytes) -> bytes:
    """Pomelo 封包: [type][3 字节大端长度][payload]"""
    n = len(payload)
    return bytes([packet_type, (n >> 16) & 0xFF, (n >> 8) & 0xFF, n & 0xFF]) + payload


def read_exact(sock: socket.socket, n: int) -> bytes:
    buf = b""
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("连接已关闭")
        buf += chunk
    return buf


def read_packet(sock: socket.socket, timeout: float = 5.0) -> tuple[int, bytes]:
    sock.settimeout(timeout)
    head = read_exact(sock, 4)
    n = (head[1] << 16) | (head[2] << 8) | head[3]
    return head[0], read_exact(sock, n) if n else b""


def encode_request(msg_id: int, route: str, data: bytes) -> bytes:
    """Pomelo Contract 编码: [flag][varint id][1 字节 route 长度][route][data]"""
    flag = MSG_REQUEST << 1              # type=Request(0), compressed=0, error=0
    route_bytes = route.encode("ascii")
    return (bytes([flag]) + encode_varint(msg_id)
            + bytes([len(route_bytes)]) + route_bytes + data)


def decode_response(payload: bytes) -> tuple[int, bytes]:
    """返回 (message id, protobuf 数据体)"""
    b = payload[0]
    mtype = (b >> 1) & 0x07
    offset = 1
    msg_id = 0
    if mtype in (MSG_REQUEST, MSG_RESPONSE):
        msg_id, offset = decode_varint(payload, offset)
    # Response 不带 route 字段, 剩余即为数据体
    return msg_id, payload[offset:]


def proto_fields(payload: bytes) -> dict[int, int]:
    """读取 protobuf 数据体中所有 varint 字段: {field_number: value}。
    嵌套/字节字段跳过, 返回首个出现值(proto 无重复标量字段)。"""
    out: dict[int, int] = {}
    if not payload:
        return out
    i = 0
    while i < len(payload):
        key, i = decode_varint(payload, i)
        fnum, wire = key >> 3, key & 0x07
        if wire == 0:
            val, i = decode_varint(payload, i)
            if fnum not in out:
                out[fnum] = val
        elif wire == 2:
            ln, i = decode_varint(payload, i)
            i += ln
        elif wire == 5:
            i += 4
        elif wire == 1:
            i += 8
        else:
            return out
    return out


def proto_field1(payload: bytes) -> int | None:
    """读取 protobuf 数据体中 field 1 的 varint 值。"""
    return proto_fields(payload).get(1)


def connect_and_handshake() -> socket.socket:
    sock = socket.create_connection((HOST, PORT), timeout=5)
    sock.sendall(make_packet(PKT_HANDSHAKE, HANDSHAKE_JSON.encode("utf-8")))
    ptype, payload = read_packet(sock)
    if ptype != PKT_HANDSHAKE:
        sock.close()
        raise RuntimeError(f"期望 Handshake(0x01), 实际 0x{ptype:02X}")
    sock.sendall(make_packet(PKT_HANDSHAKE_ACK, b""))
    return sock


def send_request_read_response(sock: socket.socket, msg_id: int,
                               route: str, body: bytes,
                               max_packets: int = 12,
                               timeout: float = 6.0) -> bytes | None:
    """发一个 Data 请求, 读到首个 Data 响应体(跳过心跳)。"""
    sock.sendall(make_packet(PKT_DATA, encode_request(msg_id, route, body)))
    for _ in range(max_packets):
        ptype, payload = read_packet(sock, timeout=timeout)
        if ptype == PKT_HEARTBEAT:
            continue
        if ptype == PKT_DATA:
            _, resp_body = decode_response(payload)
            return resp_body
    return None


def probe_enemy_drop(level_id: int, seq_id: int = 7, enemy_loot_id: int = 61283755) -> None:
    """验证服务端对 LevelEnemyDropRequest 回显 SeqID。

    背景: 客户端 RORSpawnManager 把带掉落表的 spawner 放入等待列表后,
    周期发 LevelEnemyDropRequest{SeqID=uid, EnemyLootID, LevelID};
    服务端响应 LevelEnemyDropResponse.SeqID 必须 == 请求 SeqID, 客户端
    才能按 FindIndex(uid) 解锁等待项 -> 真正刷怪。回 0 字节空响应会永远
    解锁失败(敌人不生成)。
    """
    sock = connect_and_handshake()
    try:
        body = (bytes([0x08]) + encode_varint(seq_id)          # field 1 SeqID
                + bytes([0x10]) + encode_varint(enemy_loot_id)  # field 2 EnemyLootID
                + bytes([0x18]) + encode_varint(level_id))      # field 3 LevelID
        route = "game.game.LevelEnemyDropRequest"
        resp_body = send_request_read_response(sock, 1, route, body)
        if resp_body is None:
            print(f"  [BAD] {route} 无响应")
            return
        fields = proto_fields(resp_body)
        got_seq = fields.get(1)
        got_loot = fields.get(3)
        ok = "OK " if got_seq == seq_id else "BAD"
        flag = "" if got_seq == seq_id else "  <-- SeqID 不匹配! 刷怪等待列表无法解锁"
        print(f"  [{ok}] {route:<34} 请求 SeqID={seq_id} EnemyLootID={enemy_loot_id} "
              f"LevelID={level_id}")
        print(f"       响应 SeqID={got_seq}  EnemyLootID={got_loot}  "
              f"({len(resp_body)} 字节){flag}")
        print(f"       hex: {resp_body.hex(' ')}")
    finally:
        sock.close()


def probe(level_id: int, routes: list[str]) -> None:
    sock = socket.create_connection((HOST, PORT), timeout=5)

    # --- 握手 ---
    # 服务端收到 Handshake(0x01) 后回 Handshake(0x01); 客户端须再回
    # HandshakeAck(0x02), 服务端才置 handshakeDone=true 允许后续 Data 包。
    sock.sendall(make_packet(PKT_HANDSHAKE, HANDSHAKE_JSON.encode("utf-8")))
    ptype, payload = read_packet(sock)
    if ptype != PKT_HANDSHAKE:
        print(f"  [!] 期望 Handshake(0x01), 实际 0x{ptype:02X}")
        sock.close()
        return

    sock.sendall(make_packet(PKT_HANDSHAKE_ACK, b""))

    body = bytes([0x08]) + encode_varint(level_id)   # field 1 varint = LevelID

    for idx, route in enumerate(routes, start=1):
        sock.sendall(make_packet(PKT_DATA, encode_request(idx, route, body)))

        # 读取直到拿到 Response (中途会有心跳包)
        resp_body = None
        for _ in range(12):
            ptype, payload = read_packet(sock, timeout=6.0)
            if ptype == PKT_HEARTBEAT:
                continue
            if ptype == PKT_DATA:
                msg_id, resp_body = decode_response(payload)
                break

        if resp_body is None:
            print(f"  {route:<34} 无响应")
            continue

        got = proto_field1(resp_body)
        ok = "OK " if got == level_id else "BAD"
        flag = "" if got == level_id else f"  <-- 不匹配! 客户端会卡住"
        print(f"  [{ok}] {route:<34} 请求={level_id}  响应={got}  "
              f"({len(resp_body)} 字节){flag}")
        if resp_body:
            print(f"        hex: {resp_body.hex(' ')}")

    sock.close()


def main() -> int:
    routes = ["game.game.LevelBeginRequest", "game.game.LevelResourceRequest"]
    level_ids = [int(x) for x in sys.argv[1:]] or [43400001, 43400011, 43400330]

    names = {
        43400001: "1-1_铜釜-剧情1",
        43400011: "1-1_普通",
        43400330: "城镇-主基地-新2.0 (主城)",
    }

    print(f"[*] 连接 {HOST}:{PORT}")
    for lid in level_ids:
        label = names.get(lid, "")
        print(f"\n=== 测试 LevelID={lid} {label} ===")
        try:
            probe(lid, routes)
        except Exception as exc:
            print(f"  [!] 失败: {type(exc).__name__}: {exc}")

    # 只对战斗关测刷怪解锁(主城没有 spawner 掉落请求)
    drop_level = next((lid for lid in level_ids if lid != 43400330), 43400011)
    print(f"\n=== 测试 LevelEnemyDropRequest SeqID 回显 (LevelID={drop_level}) ===")
    try:
        probe_enemy_drop(drop_level)
    except Exception as exc:
        print(f"  [!] 失败: {type(exc).__name__}: {exc}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

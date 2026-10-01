#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
audit_level_spawn.py
====================
校验「关卡 -> 房间 -> 刷怪配置」链路的数据完整性。

刷怪链路(客户端本地, 服务端不下发任何刷怪数据)
----------------------------------------------
LevelSht._StartRoom / _FixedRoom / _BattleRoomPool / _PeaceRoomPool
    -> RoomSht._EnemySet   (int[]) 敌人集合 ID 列表
       RoomSht._EnemySpawner (int[]) 刷怪器 ID 列表
       RoomSht._EnemyGroup (int[]) 敌人组资产 ID 列表

两条互斥的刷怪路径:
  A. 表驱动: 需要 _EnemySet[i] > 0 且 _EnemySpawner[i] > 0
           客户端 RoomSht.GetRoomEmySpawnerData() 会
           ExcelDataLoader.Get<EnemySetSht>(EnemySet[i])
           ExcelDataLoader.Get<SpawnerSht>(EnemySpawner[i])
           任一为 null 则静默跳过 -> 不刷怪
  B. 资产驱动: 需要 _EnemyGroup[i] > 0
           客户端 FixedMapData.LoadEnemyGroupAsset() 会
           AssetPathUtility.GetAssetFullPath(groupID)
           ResourceManager.LoadAssetsAsync<EnemyGroupAsset>(path)
           资产为 null 则 ROREnemyGroupSpawner.CheckNeedOpenSpawnBox()
           返回 false -> 状态机不推进 -> 不刷怪

用法
----
    python audit_level_spawn.py [SheetDB 目录]
"""
from __future__ import annotations

import json
import sys
from collections import Counter
from pathlib import Path

DEFAULT_SHEETDB = r"G:\IntoTheVoid_Extracted\SheetDB"


def load_sheet(sheet_dir: Path, name: str) -> dict[int, dict]:
    path = sheet_dir / name
    if not path.exists():
        sys.exit(f"缺少表文件: {path}")
    with path.open(encoding="utf-8") as fp:
        data = json.load(fp)
    return {row["_ID"]: row for row in data["rows"]}


def has_value(v) -> bool:
    """判断 int[] 字段是否含有效(非 0) ID。"""
    return isinstance(v, (list, tuple)) and any(isinstance(x, int) and x != 0 for x in v)


def positive(v) -> list[int]:
    if not isinstance(v, (list, tuple)):
        return []
    return [x for x in v if isinstance(x, int) and x != 0]


def main() -> int:
    sheet_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(DEFAULT_SHEETDB)

    print(f"[*] 读取配置表: {sheet_dir}")
    levels = load_sheet(sheet_dir, "Level_LevelShtSheet.json")
    rooms = load_sheet(sheet_dir, "Room_RoomShtSheet.json")
    spawners = load_sheet(sheet_dir, "Spawner_SpawnerShtSheet.json")
    enemy_sets = load_sheet(sheet_dir, "EnemySet_EnemySetShtSheet.json")
    print(f"    LevelSht={len(levels)}  RoomSht={len(rooms)}  "
          f"SpawnerSht={len(spawners)}  EnemySetSht={len(enemy_sets)}")

    # --- 房间维度: 判据 A / B 是否可用 ---
    room_table_ok: set[int] = set()
    room_asset_ok: set[int] = set()
    room_missing_set: set[int] = set()      # 有 Spawner 但 EnemySet 查不到
    room_missing_spawner: set[int] = set()

    for rid, room in rooms.items():
        sets = positive(room.get("_EnemySet"))
        spawns = positive(room.get("_EnemySpawner"))
        groups = positive(room.get("_EnemyGroup"))

        # 路径 A: 表驱动
        if sets and spawns:
            pairs = min(len(sets), len(spawns))
            ok = False
            for i in range(pairs):
                s_ok = sets[i] in enemy_sets
                p_ok = spawns[i] in spawners
                if s_ok and p_ok:
                    ok = True
                if not s_ok:
                    room_missing_set.add(sets[i])
                if not p_ok:
                    room_missing_spawner.add(spawns[i])
            if ok:
                room_table_ok.add(rid)

        # 路径 B: 资产驱动(资产本身不在此校验, 仅看 ID 是否配置)
        if groups:
            room_asset_ok.add(rid)

    print("\n=== 房间维度 ===")
    print(f"  路径A(表驱动 EnemySet+Spawner) 可用房间 : {len(room_table_ok)}")
    print(f"  路径B(资产驱动 EnemyGroup)      可用房间 : {len(room_asset_ok)}")
    print(f"  两条路径都不可用                        : "
          f"{len(rooms) - len(room_table_ok | room_asset_ok)}")
    if room_missing_set:
        print(f"  [!] 引用了不存在的 EnemySet ID : {sorted(room_missing_set)[:12]}"
              f" (共 {len(room_missing_set)} 个)")
    if room_missing_spawner:
        print(f"  [!] 引用了不存在的 Spawner ID  : {sorted(room_missing_spawner)[:12]}"
              f" (共 {len(room_missing_spawner)} 个)")

    # --- 关卡维度: 起始/固定/战斗房间池是否可刷怪 ---
    level_rooms_found = 0
    level_rooms_missing = set()
    spawnable_levels: list[tuple[int, str, int, str]] = []
    dead_levels: list[tuple[int, str]] = []

    for lid, lv in sorted(levels.items()):
        room_ids: list[int] = []
        for field in ("_StartRoom", "_FixedRoom",
                      "_BattleRoomPool", "_PeaceRoomPool"):
            room_ids.extend(positive(lv.get(field)))

        found = [r for r in room_ids if r in rooms]
        level_rooms_found += len(found)
        for r in room_ids:
            if r not in rooms:
                level_rooms_missing.add(r)

        usable = [r for r in found if r in room_table_ok or r in room_asset_ok]
        name = str(lv.get("_Name") or "")
        if usable:
            # 记录第一个可用房间及其路径
            first = usable[0]
            via = "A" if first in room_table_ok else "B"
            spawnable_levels.append((lid, name, first, via))
        else:
            dead_levels.append((lid, name))

    print("\n=== 关卡维度 ===")
    print(f"  关卡总数                : {len(levels)}")
    print(f"  起始/固定房间可刷怪关卡 : {len(spawnable_levels)}")
    print(f"  无可刷怪房间关卡        : {len(dead_levels)}")
    if level_rooms_missing:
        print(f"  [!] 引用了不存在的 Room ID : {sorted(level_rooms_missing)[:12]}"
              f" (共 {len(level_rooms_missing)} 个)")

    # --- 抽样展示 ---
    print("\n=== 可刷怪关卡样例 (前 12) ===")
    for lid, name, room, via in spawnable_levels[:12]:
        print(f"  {lid}  {name:<24} 起始可刷房间={room}  路径{via}")

    print("\n=== 无可刷怪房间关卡样例 (前 20) ===")
    for lid, name in dead_levels[:20]:
        lv = levels[lid]
        start = positive(lv.get("_StartRoom"))
        fixed = positive(lv.get("_FixedRoom"))
        print(f"  {lid}  {name[:22]:<24} StartRoom={start} FixedRoom={fixed} "
              f"(LevelType={lv.get('_LevelType')})")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

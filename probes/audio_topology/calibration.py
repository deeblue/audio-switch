"""Explicit host connector calibration; ambiguous mappings are never persisted."""
from __future__ import annotations
import json, os, tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from .snapshots import diff_snapshots
from .models import SnapshotRecord

@dataclass
class PortMap:
    data: dict[str, Any]
    warning: str | None = None

def load_port_map(path: Path, machine: dict[str, Any] | None = None) -> PortMap:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schema_version") != 1 or not isinstance(data.get("interfaces"), list):
        raise ValueError("接口表格式錯誤或 schema_version 不支援")
    actual = machine or {}
    expected = data.get("machine", {})
    mismatch = bool(actual and (any(expected.get(k) and actual.get(k) != expected[k] for k in ("manufacturer", "model")) or
                                (expected.get("mtm") and actual.get("mtm") and not str(actual["mtm"]).startswith(str(expected["mtm"])))))
    return PortMap(data, "主機識別不符，已停用接口校準" if mismatch else None)

def calibrate_port_map(interface_id: str, before: SnapshotRecord, after: SnapshotRecord, port_map: PortMap) -> dict[str, Any]:
    if port_map.warning:
        raise ValueError(port_map.warning)
    ports = {p["id"]: p for p in port_map.data.get("interfaces", [])}
    if interface_id not in ports: raise ValueError(f"找不到接口 ID：{interface_id}")
    diff = diff_snapshots(before, after)
    candidates = [n for n in diff.added_devnodes if n.get("location_paths") and n.get("instance_id", "").upper().startswith("USB\\") and "&MI_" not in n.get("instance_id", "").upper()]
    # Require exactly one newly observed PnP node; ContainerId and names are not evidence.
    if len(candidates) == 1:
        prefixes = sorted(set(candidates[0]["location_paths"]))
        current = ports[interface_id].setdefault("usb_location_path_prefixes", [])
        for prefix in prefixes:
            if prefix not in current: current.append(prefix)
        ports[interface_id].setdefault("calibration_sources", []).append({"source": "校準", "instance_id": candidates[0]["instance_id"], "location_paths": prefixes})
        return {"status": "calibrated", "interface_id": interface_id, "location_paths": prefixes}
    if not candidates and len(diff.added_display_paths) == 1:
        row = diff.added_display_paths[0]
        connector = {"path_id": row["path_id"], "adapter_luid": row.get("adapter_luid"),
                     "target_id": row.get("properties", {}).get("target_id"), "connector_instance": row.get("connector_instance"),
                     "output_technology": row.get("output_technology")}
        current = ports[interface_id].setdefault("display_connectors", [])
        if connector not in current: current.append(connector)
        ports[interface_id].setdefault("calibration_sources", []).append({"source": "校準", "display_connector": connector})
        return {"status": "calibrated", "interface_id": interface_id, "display_connector": connector}
    return {"status": "ambiguous" if candidates or diff.added_display_paths else "no_candidate",
            "candidate_ids": [x["instance_id"] for x in candidates] + [x["path_id"] for x in diff.added_display_paths]}

def save_port_map(path: Path, port_map: PortMap) -> None:
    fd, temp_name = tempfile.mkstemp(prefix=".ports-", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(port_map.data, stream, ensure_ascii=False, indent=2); stream.write("\n")
        os.replace(temp_name, path)
    finally:
        if os.path.exists(temp_name): os.unlink(temp_name)

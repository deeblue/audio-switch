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


_STABLE_DISPLAY_FIELDS = ("adapter_device_path", "target_id", "connector_instance", "output_technology")


def stable_display_connector(path) -> dict[str, Any]:
    get = path.get if isinstance(path, dict) else lambda key, default=None: getattr(path, key, default)
    properties = get("properties", {}) or {}
    return {
        "adapter_device_path": get("adapter_name"),
        "target_id": properties.get("target_id"),
        "connector_instance": get("connector_instance"),
        "output_technology": get("output_technology"),
    }


def display_connector_matches(path, calibrated: dict[str, Any]) -> bool:
    current = stable_display_connector(path)
    return all(current.get(field) is not None and calibrated.get(field) is not None and
               current[field] == calibrated[field] for field in _STABLE_DISPLAY_FIELDS)


def display_interface_id(path, port_map: PortMap) -> str | None:
    if port_map.warning:
        return None
    matches = [port["id"] for port in port_map.data.get("interfaces", [])
               if any(display_connector_matches(path, item) for item in port.get("display_connectors", []))]
    return matches[0] if len(matches) == 1 else None


def usb_path_type(location_paths: list[str]) -> str:
    value = " ".join(location_paths).upper()
    if "SS" in value and any(token in value for token in ("SS0", "SS1", "SS2", "SS3", "SS4", "SS5", "SS6", "SS7", "SS8", "SS9")):
        return "USB 3"
    if "HS" in value and any(token in value for token in ("HS0", "HS1", "HS2", "HS3", "HS4", "HS5", "HS6", "HS7", "HS8", "HS9")):
        return "USB 2"
    return "USB 未知"

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
    usb_nodes = diff.added_devnodes + [change["after"] for change in diff.changed_devnodes]
    candidates = [n for n in usb_nodes if n.get("location_paths") and n.get("instance_id", "").upper().startswith("USB\\") and "&MI_" not in n.get("instance_id", "").upper()]
    # Require exactly one newly observed or relocated USB parent; ContainerId and names are not evidence.
    if len(candidates) == 1:
        prefixes = sorted(set(candidates[0]["location_paths"]))
        route_type = usb_path_type(prefixes)
        current = ports[interface_id].setdefault("usb_location_path_prefixes", [])
        for prefix in prefixes:
            if prefix not in current: current.append(prefix)
        ports[interface_id].setdefault("calibration_sources", []).append({"source": "校準", "transport": route_type, "instance_id": candidates[0]["instance_id"], "location_paths": prefixes})
        return {"status": "calibrated", "interface_id": interface_id, "transport": route_type, "location_paths": prefixes}
    if not candidates and len(diff.added_display_paths) == 1:
        row = diff.added_display_paths[0]
        connector = stable_display_connector(row)
        if any(connector.get(field) is None for field in _STABLE_DISPLAY_FIELDS):
            return {"status": "no_candidate", "reason": "顯示路徑缺少穩定校準欄位", "display_connector": connector}
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

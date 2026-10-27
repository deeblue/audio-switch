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

def attachment_transport(node: dict[str, Any]) -> str:
    if node.get("instance_id", "").upper().startswith("PCI\\"):
        return "PCIe 隧道"
    return usb_path_type(node.get("location_paths", []))


def _is_attachment(node: dict[str, Any]) -> bool:
    instance_id = node.get("instance_id", "").upper()
    if "&MI_" in instance_id:
        return False
    if instance_id.startswith("USB\\"):
        return bool(node.get("location_paths"))
    if instance_id.startswith("PCI\\"):
        # A tunnelled PCIe device sits behind a hot-plug root port. Devices on the
        # root complex itself (for example the iGPU at PCI(0200)) are integrated and
        # only appear in a diff because the probe began collecting their chain.
        return any(p.upper().startswith("PCIROOT(") and p.upper().count("#PCI(") >= 2 for p in node.get("location_paths", []))
    return False


def attachment_nodes(before: SnapshotRecord, after: SnapshotRecord) -> list[dict[str, Any]]:
    """Return the topmost devnodes that appeared or moved between two snapshots."""
    diff = diff_snapshots(before, after)
    # Only a LocationPaths change is relocation evidence; other property changes
    # (for example a root hub read with a newer probe) are not.
    moved = [c["after"] for c in diff.changed_devnodes if c["before"].get("location_paths") != c["after"].get("location_paths")]
    new_nodes = diff.added_devnodes + moved
    new_ids = {n.get("instance_id", "").casefold() for n in new_nodes}
    return [n for n in new_nodes if _is_attachment(n) and (n.get("parent_id") or "").casefold() not in new_ids]

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
    candidates = attachment_nodes(before, after)
    # One physical connector exposes at most one USB 2, one USB 3 and one PCIe
    # tunnel attachment (a dock brings several at once). Two attachments on the
    # same transport mean two connections changed; ContainerId and names are not evidence.
    transports = [attachment_transport(n) for n in candidates]
    if candidates and len(set(transports)) == len(transports):
        # Screens on a dock arrive with its USB devices. Screens on a USB display adapter are placed through
        # the USB tree; the others run on the host GPU and are recorded as display connectors of this port.
        dock_connectors = [connector for connector in (stable_display_connector(row) for row in diff.added_display_paths
                                                       if not str(row.get("adapter_instance_id") or "").upper().startswith("USB\\"))
                           if all(connector.get(field) is not None for field in _STABLE_DISPLAY_FIELDS)]
        for connector in dock_connectors:
            _add_display_connector(ports[interface_id], connector)
        current = ports[interface_id].setdefault("usb_location_path_prefixes", [])
        attachments = []
        for node, route_type in zip(candidates, transports):
            prefixes = sorted(set(node["location_paths"]))
            for prefix in prefixes:
                if prefix not in current: current.append(prefix)
            attachment = {"source": "校準", "transport": route_type, "instance_id": node["instance_id"], "location_paths": prefixes}
            ports[interface_id].setdefault("calibration_sources", []).append(attachment)
            attachments.append(attachment)
        return {"status": "calibrated", "interface_id": interface_id, "transports": transports,
                "location_paths": sorted({p for a in attachments for p in a["location_paths"]}), "attachments": attachments,
                "display_connectors": dock_connectors}
    added = diff.added_display_paths
    # An HDMI connector carries one screen; USB-C and DisplayPort can carry several through a dock or MST hub.
    if not candidates and (len(added) == 1 or (len(added) > 1 and ports[interface_id].get("type") != "hdmi")):
        connectors = [stable_display_connector(row) for row in added]
        if any(connector.get(field) is None for connector in connectors for field in _STABLE_DISPLAY_FIELDS):
            return {"status": "no_candidate", "reason": "顯示路徑缺少穩定校準欄位", "display_connectors": connectors}
        for connector in connectors:
            _add_display_connector(ports[interface_id], connector)
        return {"status": "calibrated", "interface_id": interface_id, "display_connectors": connectors}
    return {"status": "ambiguous" if candidates or diff.added_display_paths else "no_candidate",
            "candidate_ids": [x["instance_id"] for x in candidates] + [x["path_id"] for x in diff.added_display_paths]}

def _add_display_connector(port: dict[str, Any], connector: dict[str, Any]) -> None:
    current = port.setdefault("display_connectors", [])
    if connector in current:
        return
    current.append(connector)
    port.setdefault("calibration_sources", []).append({"source": "校準", "display_connector": connector})

def save_port_map(path: Path, port_map: PortMap) -> None:
    fd, temp_name = tempfile.mkstemp(prefix=".ports-", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(port_map.data, stream, ensure_ascii=False, indent=2); stream.write("\n")
        os.replace(temp_name, path)
    finally:
        if os.path.exists(temp_name): os.unlink(temp_name)

"""Export immutable reference outputs for the C# Core tests."""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path

from audio_topology.calibration import PortMap, calibrate_port_map, display_interface_id
from audio_topology.classify import build_tree
from audio_topology.models import to_jsonable
from audio_topology.snapshots import load_snapshot


ROOT = Path(__file__).resolve().parent
SNAPSHOT_NAMES = (
    "port-left-usba",
    "port-right-usba",
    "port-left-usbc-front",
    "port-left-usbc-rear",
    "final-after",
)
CALIBRATION_PAIRS = (
    ("final-after", "port-left-usba", "usb-a-left"),
    ("port-left-usba", "port-right-usba", "usb-a-right"),
    ("port-right-usba", "port-left-usbc-front", "usb-c-left-front"),
    ("port-left-usbc-front", "port-left-usbc-rear", "usb-c-left-rear"),
)


def _tree_index(tree: dict) -> tuple[dict[str, tuple[str, str | None]], dict[str, tuple[str | None, str | None]]]:
    endpoints: dict[str, tuple[str, str | None]] = {}
    devices: dict[str, tuple[str | None, str | None]] = {}

    def visit(root: dict, node: dict, group_key: str | None) -> None:
        if node.get("kind") == "device":
            group_key = node.get("instance_id")
            devices[group_key or ""] = (root["id"], group_key)
        if "endpoint_id" in node:
            endpoints[node["endpoint_id"]] = (root["id"], group_key)
        for child in node.get("children", []):
            visit(root, child, group_key)

    for root in tree["roots"]:
        for child in root.get("children", []):
            visit(root, child, None)
    return endpoints, devices


def export_snapshot(name: str, ports: dict) -> dict:
    snapshot = load_snapshot(ROOT / "out" / f"{name}.json")
    port_map = PortMap(copy.deepcopy(ports))
    tree = build_tree(snapshot, port_map, include_empty=True)
    endpoint_locations, device_locations = _tree_index(tree)
    endpoints = []
    for endpoint in snapshot.endpoints:
        root_id, group_key = endpoint_locations.get(endpoint.endpoint_id, ("未辨識", None))
        category = next(
            child["category"]
            for root in tree["roots"]
            for child in _leaves(root.get("children", []))
            if child.get("endpoint_id") == endpoint.endpoint_id
        )
        if category == "A" and root_id == "未辨識":
            root_id = "USB（未設定接口）"
        interface_ids = {port["id"] for port in ports.get("interfaces", [])}
        endpoints.append({
            "endpoint_id": endpoint.endpoint_id,
            "category": category,
            "root_id": root_id,
            "interface_id": root_id if root_id in interface_ids else None,
            "group_key": group_key,
        })

    display_groups = []
    for path in snapshot.display_paths:
        adapter_path = (path.adapter_name or "").casefold()
        adapter_id = (path.adapter_instance_id or "").casefold()
        usb_node = next((n for n in snapshot.devnodes if adapter_id.startswith("usb\\") and n.instance_id.casefold() == adapter_id), None)
        if usb_node and "&mi_" in usb_node.instance_id.casefold() and usb_node.parent_id:
            usb_node = next((n for n in snapshot.devnodes if n.instance_id.casefold() == usb_node.parent_id.casefold()), usb_node)
        if usb_node is None:
            candidates = [n for n in snapshot.devnodes if n.instance_id.upper().startswith("USB\\") and "&MI_" not in n.instance_id.upper()
                          and ("USB\\" + n.instance_id.split("\\", 2)[1]).casefold() in adapter_path.replace("#", "\\")]
            usb_node = candidates[0] if len(candidates) == 1 else None
        group_key = usb_node.instance_id if usb_node else None
        interface_id = device_locations.get(group_key or "", (None, None))[0]
        if interface_id is None:
            interface_id = display_interface_id(path, port_map, snapshot.display_paths)
        display_groups.append({
            "path_id": path.path_id,
            "interface_id": interface_id,
            "group_key": group_key,
        })
    return {"snapshot": name, "endpoints": endpoints, "display_groups": display_groups}


def _leaves(nodes: list[dict]):
    for node in nodes:
        if "endpoint_id" in node:
            yield node
        yield from _leaves(node.get("children", []))


def export_calibrations(ports: dict) -> list[dict]:
    rows = []
    for before_name, after_name, interface_id in CALIBRATION_PAIRS:
        before = load_snapshot(ROOT / "out" / f"{before_name}.json")
        after = load_snapshot(ROOT / "out" / f"{after_name}.json")
        result = calibrate_port_map(interface_id, before, after, PortMap(copy.deepcopy(ports)))
        result.pop("attachments", None)
        rows.append({"before": before_name, "after": after_name, "interface_id": interface_id, "result": to_jsonable(result)})
    return rows


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT.parent / "tests" / "AudioSwitch.Core.Tests" / "Fixtures")
    args = parser.parse_args()
    ports = json.loads((ROOT / "ports.json").read_text(encoding="utf-8"))
    snapshots = [export_snapshot(name, ports) for name in SNAPSHOT_NAMES]
    output = {
        "schema_version": 1,
        "snapshots": snapshots,
        "calibrations": export_calibrations(ports),
    }
    args.output.mkdir(parents=True, exist_ok=True)
    destination = args.output / "topology-golden.json"
    destination.write_text(json.dumps(output, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(destination)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

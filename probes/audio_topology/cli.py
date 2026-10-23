"""Command line interface for the read-only probe."""
import argparse
import json
from pathlib import Path
from dataclasses import asdict

from .models import to_jsonable
from .native.core_audio import collect_audio_endpoints
from .native.pnp import resolve_endpoint_devnode, walk_ancestors, CfgMgrApi
from .native.display_config import collect_display_paths
from .native.audio_topology import collect_jacks
from .snapshots import new_snapshot, save_snapshot, load_snapshot, diff_snapshots
from .calibration import load_port_map, calibrate_port_map, save_port_map
from .classify import build_tree
from .render import render_tree

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "out"

def snapshot_path(name: str) -> Path:
    if Path(name).name != name or name in (".", ".."):
        raise ValueError("快照名稱只能是檔名，不可包含路徑")
    return OUT / (name if name.endswith(".json") else name + ".json")

def collect_snapshot():
    snapshot = new_snapshot()
    snapshot.endpoints, errors = collect_audio_endpoints()
    snapshot.errors.extend(errors)
    seen: set[str] = set()
    try:
        pnp_api = CfgMgrApi()
    except Exception as exc:
        pnp_api = None
        snapshot.errors.append({"scope": "pnp", "property": "configuration_manager", "error": f"{type(exc).__name__}: {exc}"})
    for endpoint in snapshot.endpoints:
        mmdevapi_id, mmdevapi_method = resolve_endpoint_devnode(endpoint.endpoint_id)
        jacks, jack_errors = collect_jacks(endpoint.endpoint_id,
            device_interface_resolver=pnp_api.device_interface_instance_id if pnp_api else None)
        snapshot.jacks.extend(jacks)
        snapshot.errors.extend(jack_errors)
        topology_ids = sorted({j.adapter_instance_id for j in jacks if j.adapter_instance_id})
        topology_id = topology_ids[0] if len(topology_ids) == 1 else None
        endpoint.properties["devnode_resolution_attempts"] = {
            "device_topology": {"device_interface_id": next((j.adapter_device_id for j in jacks if j.adapter_device_id), None),
                                "instance_id": topology_id, "status": "resolved" if topology_id else "unresolved"},
            "mmdevapi": {"instance_id": mmdevapi_id, "status": "resolved" if mmdevapi_id else "unresolved"},
        }
        if len(topology_ids) > 1:
            snapshot.errors.append({"scope": endpoint.endpoint_id, "property": "topology_instance_id", "error": "多個 adapter instance ID 不一致"})
        endpoint.devnode_id, endpoint.resolution_method = (topology_id, "device_topology") if topology_id else (mmdevapi_id, mmdevapi_method)
        if endpoint.devnode_id:
            nodes, errs = walk_ancestors(endpoint.devnode_id, pnp_api)
            snapshot.devnodes.extend(n for n in nodes if n.instance_id not in seen)
            seen.update(n.instance_id for n in nodes)
            snapshot.errors.extend(errs)
    snapshot.display_paths, errs = collect_display_paths()
    snapshot.errors.extend(errs)
    if pnp_api:
        for path in snapshot.display_paths:
            if not path.adapter_name:
                continue
            try:
                path.adapter_instance_id = pnp_api.device_interface_instance_id(path.adapter_name)
                if path.adapter_instance_id:
                    nodes, node_errors = walk_ancestors(path.adapter_instance_id, pnp_api)
                    snapshot.devnodes.extend(n for n in nodes if n.instance_id not in seen)
                    seen.update(n.instance_id for n in nodes)
                    snapshot.errors.extend(node_errors)
            except Exception as exc:
                path.errors.append(f"adapter PnP resolution: {type(exc).__name__}: {exc}")
    return snapshot


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="唯讀音訊裝置與實體接口拓樸探針")
    commands = parser.add_subparsers(dest="command", required=True)
    tree = commands.add_parser("tree", help="顯示接口拓樸樹")
    tree.add_argument("--all", action="store_true", help="也顯示沒有子項的機身接口")
    commands.add_parser("raw", help="輸出端點與裝置原始資料")
    json_cmd = commands.add_parser("json", help="輸出 JSON")
    json_cmd.add_argument("filename", nargs="?")
    snapshot = commands.add_parser("snapshot", help="儲存快照")
    snapshot.add_argument("name")
    diff = commands.add_parser("diff", help="比較快照")
    diff.add_argument("before")
    diff.add_argument("after")
    calibrate = commands.add_parser("calibrate", help="校準實體接口")
    calibrate.add_argument("interface_id")
    calibrate.add_argument("before")
    calibrate.add_argument("after")
    return parser

def run_command(args: argparse.Namespace, collector=None) -> int:
    if args.command == "diff":
        result = asdict(diff_snapshots(load_snapshot(snapshot_path(args.before)), load_snapshot(snapshot_path(args.after))))
        print(json.dumps(result, ensure_ascii=False, indent=2)); return 0
    if args.command == "calibrate":
        before, after = load_snapshot(snapshot_path(args.before)), load_snapshot(snapshot_path(args.after))
        pm = load_port_map(ROOT / "ports.json", to_jsonable(collect_machine_identity()))
        result = calibrate_port_map(args.interface_id, before, after, pm)
        if result.get("status") == "calibrated": save_port_map(ROOT / "ports.json", pm)
        print(json.dumps(result, ensure_ascii=False, indent=2)); return 0 if result.get("status") == "calibrated" else 2
    snapshot = (collector or collect_snapshot)()
    if args.command == "tree":
        pm = load_port_map(ROOT / "ports.json", to_jsonable(snapshot.machine))
        if pm.warning: print(f"警告：{pm.warning}")
        print(render_tree(build_tree(snapshot, pm, include_empty=args.all))); return 0
    data = json.dumps(to_jsonable(snapshot), ensure_ascii=False, indent=2)
    if args.command == "snapshot":
        print(f"快照已儲存：{save_snapshot(snapshot, args.name, OUT)}")
    elif args.command == "json":
        if args.filename:
            path = save_snapshot(snapshot, args.filename, OUT); print(f"JSON 已儲存：{path}")
        else: print(data)
    else:
        print(data)
    return 0

def collect_machine_identity():
    from .snapshots import collect_machine_identity as collect
    return collect()

def main(argv=None):
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return run_command(args)
    except Exception as exc:
        parser.exit(2, f"錯誤：{type(exc).__name__}: {exc}\n")

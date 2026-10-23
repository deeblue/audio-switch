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
    pnp_api = None
    for endpoint in snapshot.endpoints:
        endpoint.devnode_id, endpoint.resolution_method = resolve_endpoint_devnode(endpoint.endpoint_id)
        if endpoint.devnode_id:
            if pnp_api is None:
                try: pnp_api = CfgMgrApi()
                except Exception: pnp_api = False
            nodes, errs = walk_ancestors(endpoint.devnode_id, pnp_api if pnp_api is not False else None)
            snapshot.devnodes.extend(n for n in nodes if n.instance_id not in seen)
            seen.update(n.instance_id for n in nodes)
            snapshot.errors.extend(errs)
        jacks, errs = collect_jacks(endpoint.endpoint_id)
        snapshot.jacks.extend(jacks)
        snapshot.errors.extend(errs)
    snapshot.display_paths, errs = collect_display_paths()
    snapshot.errors.extend(errs)
    return snapshot


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="唯讀音訊裝置與實體接口拓樸探針")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("tree", help="顯示接口拓樸樹")
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
        print(render_tree(build_tree(snapshot, pm))); return 0
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

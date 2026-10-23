"""Snapshot storage and evidence-only comparison helpers."""
from __future__ import annotations
from collections import Counter
from dataclasses import dataclass
from datetime import datetime, timezone
import json, os, platform, tempfile
from pathlib import Path
from typing import Any
from .models import SnapshotRecord, MachineRecord, to_jsonable

@dataclass
class DiffRecord:
    added_endpoints: list[dict[str, Any]]
    removed_endpoints: list[dict[str, Any]]
    added_devnodes: list[dict[str, Any]]
    removed_devnodes: list[dict[str, Any]]
    changed_devnodes: list[dict[str, Any]]
    added_display_paths: list[dict[str, Any]]
    removed_display_paths: list[dict[str, Any]]
    common_location_prefixes: list[str]

def collect_machine_identity() -> MachineRecord:
    manufacturer = model = mtm = None
    if os.name == "nt":
        try:
            import winreg
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"HARDWARE\DESCRIPTION\System\BIOS") as key:
                vals = {name: winreg.QueryValueEx(key, name)[0] for name in ("SystemManufacturer", "SystemProductName", "SystemSKU")}
            # Lenovo reports MTM in SystemProductName and the marketing model in SystemSKU.
            manufacturer, mtm, model = vals["SystemManufacturer"], vals["SystemProductName"], vals["SystemSKU"]
            if "ThinkPad" in model and "_FM_" in model:
                model = model.split("_FM_", 1)[-1]
        except Exception:
            pass
    return MachineRecord(manufacturer or platform.uname().node, model, mtm)

def save_snapshot(snapshot: SnapshotRecord, name: str, output_dir: Path) -> Path:
    if Path(name).name != name or name in (".", ".."):
        raise ValueError("快照名稱只能是檔名，不可包含路徑")
    output_dir.mkdir(parents=True, exist_ok=True)
    path = output_dir / (name if name.endswith(".json") else name + ".json")
    payload = json.dumps(to_jsonable(snapshot), ensure_ascii=False, indent=2, sort_keys=True)
    fd, temp_name = tempfile.mkstemp(prefix=".snapshot-", suffix=".tmp", dir=output_dir)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(payload + "\n")
        os.replace(temp_name, path)
    finally:
        if os.path.exists(temp_name): os.unlink(temp_name)
    return path

def load_snapshot(path: Path) -> SnapshotRecord:
    data = json.loads(path.read_text(encoding="utf-8"))
    from .models import EndpointRecord, DeviceNodeRecord, DisplayPathRecord, JackRecord
    return SnapshotRecord(data["schema_version"], data["captured_at"], MachineRecord(**data.get("machine", {})),
        [EndpointRecord(**x) for x in data.get("endpoints", [])],
        [DeviceNodeRecord(**x) for x in data.get("devnodes", [])],
        [DisplayPathRecord(**x) for x in data.get("display_paths", [])], data.get("errors", []),
        [JackRecord(**x) for x in data.get("jacks", [])])

def _changes(before: list[Any], after: list[Any], id_field: str) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    b, a = [to_jsonable(x) for x in before], [to_jsonable(x) for x in after]
    bc, ac = Counter(x.get(id_field) for x in b), Counter(x.get(id_field) for x in a)
    added, removed = [], []
    remaining_b, remaining_a = bc.copy(), ac.copy()
    for value in a:
        key = value.get(id_field)
        if remaining_b[key]: remaining_b[key] -= 1
        else: added.append(value)
    for value in b:
        key = value.get(id_field)
        if remaining_a[key]: remaining_a[key] -= 1
        else: removed.append(value)
    return added, removed

def _changed(before: list[Any], after: list[Any], id_field: str) -> list[dict[str, Any]]:
    old_by_id: dict[Any, list[dict[str, Any]]] = {}
    for value in (to_jsonable(x) for x in before):
        old_by_id.setdefault(value.get(id_field), []).append(value)
    seen: Counter[Any] = Counter()
    changed = []
    for value in (to_jsonable(x) for x in after):
        key = value.get(id_field)
        index = seen[key]
        seen[key] += 1
        matches = old_by_id.get(key, [])
        if index < len(matches) and value != matches[index]:
            changed.append({id_field: key, "before": matches[index], "after": value})
    return changed

def diff_snapshots(before: SnapshotRecord, after: SnapshotRecord) -> DiffRecord:
    ae, re = _changes(before.endpoints, after.endpoints, "endpoint_id")
    ad, rd = _changes(before.devnodes, after.devnodes, "instance_id")
    cd = _changed(before.devnodes, after.devnodes, "instance_id")
    ap, rp = _changes(before.display_paths, after.display_paths, "path_id")
    old_paths = {p for n in before.devnodes for p in n.location_paths}
    new_paths = {p for n in after.devnodes for p in n.location_paths} - old_paths
    prefixes = set()
    for new in new_paths:
        for old in old_paths:
            bits = []
            for x, y in zip(old.split("#"), new.split("#")):
                if x != y: break
                bits.append(x)
            if bits and len(bits) < len(new.split("#")):
                prefixes.add("#".join(bits))
    return DiffRecord(ae, re, ad, rd, cd, ap, rp, sorted(prefixes))

def new_snapshot() -> SnapshotRecord:
    return SnapshotRecord(1, datetime.now(timezone.utc).isoformat(), collect_machine_identity())

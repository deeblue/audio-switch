"""Conservative evidence-based endpoint classification."""
from dataclasses import dataclass, field
from typing import Any
from .models import EndpointRecord, DeviceNodeRecord, JackRecord, DisplayPathRecord, SnapshotRecord
from .calibration import PortMap

@dataclass
class ClassificationRecord:
    endpoint_id: str
    category: str
    interface_id: str | None = None
    ancestors: list[str] = field(default_factory=list)
    evidence: list[str] = field(default_factory=list)
    unresolved: list[str] = field(default_factory=list)

def classify_endpoint(endpoint: EndpointRecord, nodes: list[DeviceNodeRecord], jacks: list[JackRecord], sink_match: dict[str, Any] | None, paths: list[DisplayPathRecord], port_map: PortMap) -> ClassificationRecord:
    ids = [n.instance_id.upper() for n in nodes]
    ancestors = [n.instance_id for n in nodes]
    if any("BTH" in i or "BLUETOOTH" in i for i in ids):
        return ClassificationRecord(endpoint.endpoint_id, "D", ancestors=ancestors, evidence=["Windows PnP instance ID"])
    if any("ROOT\\MEDIA" in i or "SWD\\MMDEVAPI" in i for i in ids) and not any(i.startswith("USB\\") for i in ids):
        return ClassificationRecord(endpoint.endpoint_id, "D", ancestors=ancestors, evidence=[], unresolved=["尚無足夠證據區分內建、虛擬或其他來源"])
    usb_nodes = [n for n in nodes if n.instance_id.upper().startswith("USB\\")]
    if usb_nodes:
        for port in ([] if port_map.warning else port_map.data.get("interfaces", [])):
            prefixes = port.get("usb_location_path_prefixes", [])
            for node in usb_nodes:
                if any(path.casefold().startswith(prefix.casefold()) for path in node.location_paths for prefix in prefixes):
                    return ClassificationRecord(endpoint.endpoint_id, "A", port["id"], ancestors, ["USB PnP ancestor", "校準 LocationPaths"])
        return ClassificationRecord(endpoint.endpoint_id, "A", ancestors=ancestors, evidence=["USB PnP ancestor"], unresolved=["此 USB 裝置尚未校準到機身接口"])
    if sink_match and sink_match.get("matched"):
        return ClassificationRecord(endpoint.endpoint_id, "B", ancestors=ancestors, evidence=[str(sink_match.get("method"))], unresolved=["顯示 connector 尚未校準到機身接口"])
    if jacks:
        return ClassificationRecord(endpoint.endpoint_id, "C", ancestors=ancestors, evidence=["IKsJackDescription"])
    return ClassificationRecord(endpoint.endpoint_id, "D", ancestors=ancestors, unresolved=["缺少 USB、顯示 sink 或 jack 證據"])

def build_tree(snapshot: SnapshotRecord, port_map: PortMap) -> dict[str, Any]:
    interfaces = [] if port_map.warning else port_map.data.get("interfaces", [])
    roots = [{"id": p["id"], "name": p["name"], "source": "OEM", "children": []} for p in interfaces]
    roots += [{"id": x, "name": x, "source": "未知", "children": []} for x in ("內建", "藍牙／無線", "虛擬", "未辨識")]
    by_id = {x["id"]: x for x in roots}
    index = {n.instance_id.casefold(): n for n in snapshot.devnodes}
    for endpoint in snapshot.endpoints:
        chain, current, seen = [], endpoint.devnode_id.casefold() if endpoint.devnode_id else None, set()
        while current and current not in seen and current in index:
            seen.add(current); chain.append(index[current]); current = index[current].parent_id
            current = current.casefold() if current else None
        match = classify_endpoint(endpoint, chain, snapshot.jacks, None, snapshot.display_paths, port_map)
        root = by_id.get(match.interface_id, by_id["未辨識"])
        guid = endpoint.endpoint_id.rsplit(".", 1)[-1].strip("{}")
        short_id = guid[:8] if len(guid) >= 8 else endpoint.endpoint_id[-8:]
        node = {"endpoint_id": endpoint.endpoint_id, "short_id": short_id, "name": endpoint.friendly_name or "（無名稱）", "flow": endpoint.flow, "category": match.category, "source": "校準" if match.interface_id else "未知", "evidence": match.evidence, "unresolved": match.unresolved}
        root["children"].append(node)
    return {"roots": roots}

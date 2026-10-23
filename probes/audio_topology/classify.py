"""Conservative evidence-based endpoint classification."""
from dataclasses import dataclass, field
from typing import Any
from .models import EndpointRecord, DeviceNodeRecord, JackRecord, DisplayPathRecord, SnapshotRecord
from .calibration import PortMap, display_interface_id, usb_path_type
from .native.display_config import match_sink_to_display

@dataclass
class ClassificationRecord:
    endpoint_id: str
    category: str
    interface_id: str | None = None
    ancestors: list[str] = field(default_factory=list)
    evidence: list[str] = field(default_factory=list)
    unresolved: list[str] = field(default_factory=list)

def classify_endpoint(endpoint: EndpointRecord, nodes: list[DeviceNodeRecord], jacks: list[JackRecord], sink_match: dict[str, Any] | None, paths: list[DisplayPathRecord], port_map: PortMap) -> ClassificationRecord:
    # The endpoint's own software devnode is a transport wrapper, not its source class.
    ids = [n.instance_id.upper() for n in nodes if not n.instance_id.upper().startswith("SWD\\MMDEVAPI\\")]
    ancestors = [n.instance_id for n in nodes]
    if any("BTH" in i or "BLUETOOTH" in i for i in ids):
        return ClassificationRecord(endpoint.endpoint_id, "D", ancestors=ancestors, evidence=["Windows PnP instance ID"])
    usb_nodes = [n for n in nodes if n.instance_id.upper().startswith("USB\\")]
    if usb_nodes:
        for port in ([] if port_map.warning else port_map.data.get("interfaces", [])):
            prefixes = port.get("usb_location_path_prefixes", [])
            for node in usb_nodes:
                if any(path.casefold().startswith(prefix.casefold()) for path in node.location_paths for prefix in prefixes):
                    return ClassificationRecord(endpoint.endpoint_id, "A", port["id"], ancestors, ["USB PnP ancestor", "校準 LocationPaths"])
        return ClassificationRecord(endpoint.endpoint_id, "A", ancestors=ancestors, evidence=["USB PnP ancestor"], unresolved=["此 USB 裝置尚未校準到機身接口"])
    if sink_match and sink_match.get("matched"):
        interface_id = sink_match.get("interface_id")
        unresolved = [] if interface_id else ["顯示 connector 尚未校準到機身接口"]
        return ClassificationRecord(endpoint.endpoint_id, "B", interface_id, ancestors, [str(sink_match.get("method"))], unresolved)
    has_jack_description = any(record.jack for record in jacks)
    if has_jack_description or any(i.startswith(("INTELAUDIO\\", "HDAUDIO\\")) for i in ids):
        evidence = ["IKsJackDescription"] if has_jack_description else ["Windows PnP audio adapter"]
        return ClassificationRecord(endpoint.endpoint_id, "C", ancestors=ancestors, evidence=evidence)
    virtual = any(i.startswith(("ROOT\\MEDIA", "SWD\\", "ROOT\\SYSTEM")) for i in ids)
    reason = "虛擬／軟體裝置" if virtual else "缺少 USB、顯示 sink 或 jack 證據"
    return ClassificationRecord(endpoint.endpoint_id, "D", ancestors=ancestors, unresolved=[reason])

# EPcxConnectionType: 3.5mm, 1/4 inch, combination. EPxJackConnection: jack, integrated and jack.
_HOST_JACK_CONNECTION_TYPES = {1, 2, 11}
_HOST_JACK_PORT_CONNECTIONS = {0, 2}

def is_physical_jack(jack: dict[str, Any]) -> bool:
    return jack.get("connection_type") in _HOST_JACK_CONNECTION_TYPES and jack.get("port_connection") in _HOST_JACK_PORT_CONNECTIONS

_PLD_PANELS = {0: "上", 1: "下", 2: "左", 3: "右", 4: "前", 5: "後"}
_PLD_SHAPES = {0: "圓形", 1: "橢圓", 2: "正方形", 3: "直向矩形", 4: "橫向矩形", 5: "直向梯形", 6: "橫向梯形", 8: "倒角"}

def describe_pld(pld: dict[str, Any] | None) -> str | None:
    if not pld or pld.get("revision") is None:
        return None
    panel = _PLD_PANELS.get(pld.get("panel"), "未知")
    shape = _PLD_SHAPES.get(pld.get("shape"), "未知")
    return f"_PLD：面板 {panel}、形狀 {shape}、群組 {pld.get('group_token')}／位置 {pld.get('group_position')}"

def build_tree(snapshot: SnapshotRecord, port_map: PortMap, include_empty: bool = False) -> dict[str, Any]:
    interfaces = [] if port_map.warning else port_map.data.get("interfaces", [])
    roots = [{"id": p["id"], "name": p["name"], "source": p.get("position_source", "未知"), "children": []} for p in interfaces]
    roots += [{"id": x, "name": x, "source": "未知", "children": []} for x in ("內建", "藍牙／無線", "虛擬", "未辨識")]
    by_id = {x["id"]: x for x in roots}
    index = {n.instance_id.casefold(): n for n in snapshot.devnodes}
    all_chains = {}
    classifications = {}
    endpoint_nodes = {}
    endpoint_roots = {}
    for endpoint in snapshot.endpoints:
        chain, current, seen = [], endpoint.devnode_id.casefold() if endpoint.devnode_id else None, set()
        while current and current not in seen and current in index:
            seen.add(current); chain.append(index[current]); current = index[current].parent_id
            current = current.casefold() if current else None
        all_chains[endpoint.endpoint_id] = chain
        endpoint_jacks = [j for j in snapshot.jacks if j.endpoint_id == endpoint.endpoint_id]
        sink_record = next((j.sink for j in endpoint_jacks if j.sink), None)
        sink_match = match_sink_to_display(sink_record, snapshot.display_paths) if sink_record else None
        if sink_match and sink_match.get("matched"):
            path = next((p for p in snapshot.display_paths if p.path_id in sink_match.get("candidate_ids", [])), None)
            sink_match["interface_id"] = display_interface_id(path, port_map) if path else None
        match = classify_endpoint(endpoint, chain, endpoint_jacks, sink_match, snapshot.display_paths, port_map)
        source = "校準" if match.interface_id else ("Windows" if match.evidence else "未知")
        if match.category == "C" and any(is_physical_jack(j.jack) for j in endpoint_jacks):
            # Integrated speakers and microphones also report IsConnected, so the
            # connector type and port connection decide whether this is a host jack.
            match.interface_id = next((p["id"] for p in interfaces if p.get("type") == "audio-jack"), None)
            match.evidence.append("IKsJackDescription 實體插孔")
            source = "Windows"
        classifications[endpoint.endpoint_id] = match
        guid = endpoint.endpoint_id.rsplit(".", 1)[-1].strip("{}")
        short_id = guid[:8] if len(guid) >= 8 else endpoint.endpoint_id[-8:]
        endpoint_nodes[endpoint.endpoint_id] = {"endpoint_id": endpoint.endpoint_id, "short_id": short_id, "name": endpoint.friendly_name or "（無名稱）", "flow": endpoint.flow, "category": match.category, "source": source, "evidence": match.evidence, "unresolved": match.unresolved}

    # Group USB endpoints by the nearest USB function/device devnode. This hides
    # intermediate hub/composite nodes while retaining a stable device identity.
    usb_groups = {}
    for endpoint in snapshot.endpoints:
        match = classifications[endpoint.endpoint_id]
        chain = all_chains[endpoint.endpoint_id]
        usb = next((n for n in chain if n.instance_id.upper().startswith("USB\\VID_") and n.location_paths and "&MI_" not in n.instance_id.upper()), None)
        if usb is None:
            usb = next((n for n in chain if n.instance_id.upper().startswith("USB\\") and "&MI_" not in n.instance_id.upper()), None)
        default_root = "內建" if match.category == "C" else ("藍牙／無線" if any("BTH" in n.instance_id.upper() for n in chain) else ("虛擬" if match.category == "D" and "虛擬／軟體裝置" in match.unresolved else "未辨識"))
        root = by_id.get(match.interface_id, by_id[default_root])
        endpoint_roots[endpoint.endpoint_id] = root["id"]
        leaf = endpoint_nodes[endpoint.endpoint_id]
        if usb:
            key = (root["id"], usb.instance_id.casefold())
            group = usb_groups.get(key)
            if group is None:
                function_name = next((n.name for n in snapshot.devnodes if n.parent_id and n.parent_id.casefold() == usb.instance_id.casefold() and n.name and not n.name.casefold().startswith(("36fc", "4d36"))), None)
                label = function_name or (usb.name if usb.name and not usb.name.casefold().startswith(("36fc", "4d36")) else "USB 裝置")
                group = {"name": label, "kind": "device", "source": "Windows", "instance_id": usb.instance_id,
                         "pld": describe_pld(usb.properties.get("physical_device_location_parsed")), "children": []}
                root["children"].append(group); usb_groups[key] = group
            group["children"].append(leaf)
        else:
            root["children"].append(leaf)

    # USB display adapters (for example the observed 7C adapter) are associated
    # only when their adapter path contains the same PnP USB instance identity.
    for path in snapshot.display_paths:
        adapter_path = (path.adapter_name or "").casefold()
        adapter_id = (path.adapter_instance_id or "").casefold()
        usb_node = index.get(adapter_id) if adapter_id.startswith("usb\\") else None
        if usb_node is None:
            # Fallback when only the adapter path is known: match the USB device
            # hardware ID, skipping interface (&MI_) nodes that would duplicate it.
            usb_candidates = [n for n in snapshot.devnodes if n.instance_id.upper().startswith("USB\\") and "&MI_" not in n.instance_id.upper() and
                              ("USB\\" + n.instance_id.split("\\", 2)[1]).casefold() in adapter_path.replace("#", "\\")]
            usb_node = usb_candidates[0] if len(usb_candidates) == 1 else None
        if usb_node and "&MI_" in usb_node.instance_id.upper() and usb_node.parent_id:
            usb_node = index.get(usb_node.parent_id.casefold(), usb_node)
        related = []
        for endpoint in snapshot.endpoints:
            chain = all_chains[endpoint.endpoint_id]
            if usb_node and any(n.instance_id.casefold() == usb_node.instance_id.casefold() for n in chain):
                related.append(endpoint)
        calibrated_id = display_interface_id(path, port_map)
        usb_root_id = next((endpoint_roots[e.endpoint_id] for e in related), None)
        interface_id = usb_root_id or calibrated_id
        if not interface_id and usb_node:
            interface_id = usb_root_id
        if not interface_id: continue
        root = by_id.get(interface_id)
        if not root:
            continue
        device = next((g for g in root["children"] if usb_node and g.get("instance_id", "").casefold() == usb_node.instance_id.casefold()), None)
        if device is None:
            device = {"name": path.target_name or "顯示器", "kind": "device", "source": "Windows", "children": []}
            root["children"].append(device)
        device["children"].append({"name": path.target_name or "顯示器", "kind": "display", "source": "Windows", "target_id": path.properties.get("target_id")})

    if not include_empty:
        roots = [root for root in roots if root["children"]]
    return {"roots": roots}

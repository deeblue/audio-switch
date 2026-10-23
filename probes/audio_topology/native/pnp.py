"""Best-effort Windows Configuration Manager PnP ancestor collection."""
from __future__ import annotations

import ctypes
import sys
import uuid
from ctypes import wintypes
from typing import Any

from ..models import DeviceNodeRecord


def resolve_endpoint_devnode(endpoint_id: str, topology_device_id: str | None = None) -> tuple[str | None, str]:
    if topology_device_id:
        return topology_device_id, "device_topology"
    marker = "SWD#MMDEVAPI#"
    if marker in endpoint_id.upper():
        tail = endpoint_id[endpoint_id.upper().index(marker) + len(marker):].split("#")[0]
        return "SWD\\MMDEVAPI\\" + tail.replace("#", "\\"), "mmdevapi_devnode"
    if endpoint_id.startswith("{0.0.") or endpoint_id.startswith("{0.1."):
        return "SWD\\MMDEVAPI\\" + endpoint_id, "mmdevapi_devnode"
    return None, "unresolved"


class _GUID(ctypes.Structure):
    _fields_ = [("Data1", wintypes.DWORD), ("Data2", wintypes.WORD), ("Data3", wintypes.WORD), ("Data4", ctypes.c_ubyte * 8)]

    @classmethod
    def parse(cls, value: str):
        parsed = uuid.UUID(value)
        return cls(parsed.time_low, parsed.time_mid, parsed.time_hi_version,
                   (ctypes.c_ubyte * 8)(*parsed.bytes[8:]))


class _DEVPROPKEY(ctypes.Structure):
    _fields_ = [("fmtid", _GUID), ("pid", wintypes.DWORD)]


_DEVICE_GUID = "A45C254E-DF1C-4EFD-8020-67D146A850E0"
_CONTAINER_GUID = "8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"
_DEVPROPS = {
    "friendly_name": (_DEVICE_GUID, 14),
    "device_name": (_DEVICE_GUID, 10),
    "bus_reported_description": (_DEVICE_GUID, 4),
    "class": (_DEVICE_GUID, 9),
    "location_info": (_DEVICE_GUID, 15),
    "location_paths": (_DEVICE_GUID, 37),
    "container_id": (_CONTAINER_GUID, 2),
}


class CfgMgrApi:
    def __init__(self):
        if sys.platform != "win32":
            raise OSError("Configuration Manager is available only on Windows")
        self.dll = ctypes.WinDLL("CfgMgr32", use_last_error=True)
        self._property_fn = self.dll.CM_Get_DevNode_PropertyW
        self._property_fn.restype = wintypes.ULONG
        self._property_fn.argtypes = [wintypes.ULONG, ctypes.POINTER(_DEVPROPKEY), ctypes.POINTER(wintypes.ULONG),
                                      ctypes.c_void_p, ctypes.POINTER(wintypes.ULONG), wintypes.ULONG]

    def locate(self, instance_id: str) -> int:
        devinst = wintypes.ULONG()
        result = self.dll.CM_Locate_DevNodeW(ctypes.byref(devinst), ctypes.c_wchar_p(instance_id), 0)
        if result != 0:
            raise OSError(f"CM_Locate_DevNodeW returned CR 0x{result:04X}")
        return devinst.value

    def device_id(self, devinst: int) -> str:
        buf = ctypes.create_unicode_buffer(4096)
        result = self.dll.CM_Get_Device_IDW(devinst, buf, len(buf), 0)
        if result != 0:
            raise OSError(f"CM_Get_Device_IDW returned CR 0x{result:04X}")
        return buf.value

    def parent(self, devinst: int) -> int | None:
        parent = wintypes.ULONG()
        result = self.dll.CM_Get_Parent(ctypes.byref(parent), devinst, 0)
        if result == 0:
            return parent.value
        if result == 0x0D:  # CR_NO_SUCH_DEVNODE means root
            return None
        raise OSError(f"CM_Get_Parent returned CR 0x{result:04X}")

    def _property(self, devinst: int, fmtid: str, pid: int) -> tuple[Any, int]:
        key = _DEVPROPKEY(_GUID.parse(fmtid), pid)
        kind, size = wintypes.ULONG(), wintypes.ULONG(0)
        status = self._property_fn(devinst, ctypes.byref(key), ctypes.byref(kind), None, ctypes.byref(size), 0)
        if status not in (0, 0x1A) or not size.value:
            raise OSError(f"CM_Get_DevNode_PropertyW size query CR 0x{status:04X}")
        buf = ctypes.create_string_buffer(size.value)
        status = self._property_fn(devinst, ctypes.byref(key), ctypes.byref(kind), buf, ctypes.byref(size), 0)
        if status != 0:
            raise OSError(f"CM_Get_DevNode_PropertyW CR 0x{status:04X}")
        raw = bytes(buf.raw[:size.value])
        if kind.value == 0x2012:  # DEVPROP_TYPE_STRING_LIST
            return [item for item in raw.decode("utf-16-le", errors="replace").split("\0") if item], kind.value
        prop_type = kind.value & 0xFFF
        if prop_type == 0x12:  # DEVPROP_TYPE_STRING
            return raw.decode("utf-16-le", errors="replace").rstrip("\0"), kind.value
        if prop_type == 0x0D and len(raw) >= 16:  # DEVPROP_TYPE_GUID
            return str(uuid.UUID(bytes_le=raw[:16])), kind.value
        if prop_type == 0x07 and len(raw) >= 4:  # DEVPROP_TYPE_UINT32
            return int.from_bytes(raw[:4], "little"), kind.value
        if prop_type == 0x03:  # DEVPROP_TYPE_BINARY
            return raw, kind.value
        return {"type": int(kind.value), "raw": raw}, kind.value

    def properties(self, devinst: int) -> dict[str, Any]:
        props: dict[str, Any] = {}
        errors: dict[str, str] = {}
        for name, (fmtid, pid) in _DEVPROPS.items():
            try:
                value, prop_type = self._property(devinst, fmtid, pid)
                props[name] = value
                props.setdefault("property_types", {})[name] = prop_type
            except Exception as exc:
                errors[name] = f"{type(exc).__name__}: {exc}"
        for name in ("removal_policy", "capabilities", "physical_device_location"):
            errors[name] = "此探針版本尚未映射該 DEVPKEY"
        props["location_paths"] = props.get("location_paths", [])
        props["errors"] = errors
        return props


def walk_ancestors(instance_id: str, api: CfgMgrApi | None = None) -> tuple[list[DeviceNodeRecord], list[dict[str, str]]]:
    errors: list[dict[str, str]] = []
    nodes: list[DeviceNodeRecord] = []
    try:
        api = api or CfgMgrApi()
        current = api.locate(instance_id)
    except Exception as exc:
        return [], [{"scope": instance_id, "property": "locate", "error": f"{type(exc).__name__}: {exc}"}]
    seen: set[int] = set()
    while current not in seen:
        seen.add(current)
        try:
            node_id = api.device_id(current)
            props = api.properties(current)
            if props.get("errors"):
                errors.extend({"scope": node_id, "property": key, "error": message} for key, message in props["errors"].items())
            nodes.append(DeviceNodeRecord(node_id, name=props.get("friendly_name") or props.get("device_name"), properties=props,
                                          location_paths=props.get("location_paths", []),
                                          container_id=props.get("container_id")))
            parent = api.parent(current)
            if parent is None:
                break
            nodes[-1].parent_id = api.device_id(parent)
            current = parent
        except Exception as exc:
            errors.append({"scope": nodes[-1].instance_id if nodes else instance_id, "property": "ancestor", "error": f"{type(exc).__name__}: {exc}"})
            break
    else:
        errors.append({"scope": nodes[-1].instance_id, "property": "parent", "error": "parent cycle detected"})
    return nodes, errors

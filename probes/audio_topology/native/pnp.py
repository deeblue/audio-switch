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
_PHYSICAL_LOCATION_GUID = "540B947E-8B40-45BC-A8A2-6A0B894CBDA2"
_DEVICE_INTERFACE_GUID = "78C34FC8-104A-4ACA-9EA4-524D52996E57"
_DEVPROPS = {
    "friendly_name": (_DEVICE_GUID, 14),
    "device_name": (_DEVICE_GUID, 10),
    "bus_reported_description": (_DEVICE_GUID, 4),
    "class": (_DEVICE_GUID, 9),
    "location_info": (_DEVICE_GUID, 15),
    "location_paths": (_DEVICE_GUID, 37),
    "container_id": (_CONTAINER_GUID, 2),
    "capabilities": (_DEVICE_GUID, 17),
    "removal_policy": (_DEVICE_GUID, 33),
    "physical_device_location": (_PHYSICAL_LOCATION_GUID, 9),
}


def decode_capabilities(value: int) -> dict[str, Any]:
    """Decode the CM_DEVCAP_REMOVABLE flag while preserving the raw bitfield."""
    return {"raw": int(value), "removable": bool(int(value) & 0x00000004)}


def parse_physical_device_location(raw: bytes) -> dict[str, Any]:
    """Parse the ACPI _PLD revision 1 four-DWORD layout."""
    if len(raw) < 16:
        return {"revision": None, "error": f"_PLD requires 16 bytes, received {len(raw)}"}
    dword1, dword2, dword3, dword4 = (int.from_bytes(raw[i:i + 4], "little") for i in range(0, 16, 4))
    return {
        "revision": dword1 & 0x7F,
        "ignore_color": bool((dword1 >> 7) & 1),
        "red": (dword1 >> 8) & 0xFF,
        "green": (dword1 >> 16) & 0xFF,
        "blue": (dword1 >> 24) & 0xFF,
        "width": dword2 & 0xFFFF,
        "height": (dword2 >> 16) & 0xFFFF,
        "user_visible": bool(dword3 & 1),
        "dock": bool((dword3 >> 1) & 1),
        "lid": bool((dword3 >> 2) & 1),
        "panel": (dword3 >> 3) & 0x7,
        "vertical_position": (dword3 >> 6) & 0x3,
        "horizontal_position": (dword3 >> 8) & 0x3,
        "shape": (dword3 >> 10) & 0xF,
        "group_orientation": (dword3 >> 14) & 1,
        "group_token": (dword3 >> 15) & 0xFF,
        "group_position": (dword3 >> 23) & 0xFF,
        "bay": bool((dword3 >> 31) & 1),
        "ejectable": bool(dword4 & 1),
        "ospm_eject_required": bool((dword4 >> 1) & 1),
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
        self._interface_property_fn = self.dll.CM_Get_Device_Interface_PropertyW
        self._interface_property_fn.restype = wintypes.ULONG
        self._interface_property_fn.argtypes = [wintypes.LPCWSTR, ctypes.POINTER(_DEVPROPKEY),
                                                ctypes.POINTER(wintypes.ULONG), ctypes.c_void_p,
                                                ctypes.POINTER(wintypes.ULONG), wintypes.ULONG]

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
        if prop_type in (0x06, 0x07) and len(raw) >= 4:  # DEVPROP_TYPE_INT32 / UINT32
            return int.from_bytes(raw[:4], "little"), kind.value
        if prop_type == 0x03:  # DEVPROP_TYPE_BINARY
            return raw, kind.value
        return {"type": int(kind.value), "raw": raw}, kind.value

    def device_interface_instance_id(self, interface_path: str) -> str:
        key = _DEVPROPKEY(_GUID.parse(_DEVICE_INTERFACE_GUID), 256)
        kind, size = wintypes.ULONG(), wintypes.ULONG(0)
        status = self._interface_property_fn(interface_path, ctypes.byref(key), ctypes.byref(kind), None,
                                             ctypes.byref(size), 0)
        if status not in (0, 0x1A) or not size.value:
            raise OSError(f"CM_Get_Device_Interface_PropertyW size query CR 0x{status:04X}")
        buffer = ctypes.create_string_buffer(size.value)
        status = self._interface_property_fn(interface_path, ctypes.byref(key), ctypes.byref(kind), buffer,
                                             ctypes.byref(size), 0)
        if status != 0:
            raise OSError(f"CM_Get_Device_Interface_PropertyW CR 0x{status:04X}")
        if (kind.value & 0xFFF) != 0x12:
            raise OSError(f"CM_Get_Device_Interface_PropertyW returned DEVPROP_TYPE 0x{kind.value:04X}, expected STRING")
        return bytes(buffer.raw[:size.value]).decode("utf-16-le", errors="replace").rstrip("\0")

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
        if "capabilities" in props:
            props["removable"] = decode_capabilities(props["capabilities"])["removable"]
        if isinstance(props.get("physical_device_location"), bytes):
            props["physical_device_location_parsed"] = parse_physical_device_location(props["physical_device_location"])
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

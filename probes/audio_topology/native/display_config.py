"""Query active Windows display paths without changing display configuration."""
import ctypes
from ctypes import wintypes
import sys

class _LUID(ctypes.Structure):
    _fields_ = [("LowPart", wintypes.DWORD), ("HighPart", wintypes.LONG)]
class _HEADER(ctypes.Structure):
    _fields_ = [("type", wintypes.UINT), ("size", wintypes.UINT), ("adapterId", _LUID), ("id", wintypes.UINT)]
class _SOURCE_NAME(ctypes.Structure):
    _fields_ = [("header", _HEADER), ("viewGdiDeviceName", wintypes.WCHAR * 32)]
class _TARGET_NAME(ctypes.Structure):
    _fields_ = [("header", _HEADER), ("flags", wintypes.UINT), ("outputTechnology", wintypes.UINT),
                ("edidManufactureId", wintypes.USHORT), ("edidProductCodeId", wintypes.USHORT),
                ("connectorInstance", wintypes.UINT), ("monitorFriendlyDeviceName", wintypes.WCHAR * 64),
                ("monitorDevicePath", wintypes.WCHAR * 128)]
class _ADAPTER_NAME(ctypes.Structure):
    _fields_ = [("header", _HEADER), ("adapterDevicePath", wintypes.WCHAR * 128)]
class _REGION(ctypes.Structure):
    _fields_ = [("cx", wintypes.UINT), ("cy", wintypes.UINT)]
class _RATIONAL(ctypes.Structure):
    _fields_ = [("Numerator", wintypes.UINT), ("Denominator", wintypes.UINT)]
class _SIGNAL(ctypes.Structure):
    _fields_ = [("pixelRate", ctypes.c_ulonglong), ("hSyncFreq", _RATIONAL), ("vSyncFreq", _RATIONAL),
                ("activeSize", _REGION), ("totalSize", _REGION), ("videoStandard", wintypes.UINT), ("scanLineOrdering", wintypes.UINT)]
class _SOURCE_MODE(ctypes.Structure):
    _fields_ = [("width", wintypes.UINT), ("height", wintypes.UINT), ("pixelFormat", wintypes.UINT), ("positionX", wintypes.LONG), ("positionY", wintypes.LONG)]
class _TARGET_MODE(ctypes.Structure):
    _fields_ = [("targetVideoSignalInfo", _SIGNAL)]
class _MODE_UNION(ctypes.Union):
    _fields_ = [("targetMode", _TARGET_MODE), ("sourceMode", _SOURCE_MODE), ("raw", ctypes.c_byte * 48)]
class _MODE_INFO(ctypes.Structure):
    _fields_ = [("infoType", wintypes.UINT), ("id", wintypes.UINT), ("adapterId", _LUID), ("mode", _MODE_UNION)]
class _PATH_SOURCE(ctypes.Structure):
    _fields_ = [("adapterId", _LUID), ("id", wintypes.UINT), ("modeInfoIdx", wintypes.UINT), ("statusFlags", wintypes.UINT)]
class _PATH_TARGET(ctypes.Structure):
    _fields_ = [("adapterId", _LUID), ("id", wintypes.UINT), ("modeInfoIdx", wintypes.UINT), ("outputTechnology", wintypes.UINT),
                ("rotation", wintypes.UINT), ("scaling", wintypes.UINT), ("refreshRate", _RATIONAL),
                ("scanLineOrdering", wintypes.UINT), ("targetAvailable", wintypes.BOOL), ("statusFlags", wintypes.UINT)]
class _PATH_INFO(ctypes.Structure):
    _fields_ = [("sourceInfo", _PATH_SOURCE), ("targetInfo", _PATH_TARGET), ("flags", wintypes.UINT)]

def _luid(luid): return {"high": int(luid.HighPart), "low": int(luid.LowPart)}

def _query_windows():
    from . import display_config as _self
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    flags = 0x2  # QDC_ONLY_ACTIVE_PATHS
    path_count, mode_count = wintypes.UINT(), wintypes.UINT()
    rc = user32.GetDisplayConfigBufferSizes(flags, ctypes.byref(path_count), ctypes.byref(mode_count))
    if rc: raise OSError(f"GetDisplayConfigBufferSizes returned {rc}")
    for _ in range(3):
        paths = (_PATH_INFO * max(1, path_count.value))()
        modes = (_MODE_INFO * max(1, mode_count.value))()
        pcount, mcount = wintypes.UINT(path_count.value), wintypes.UINT(mode_count.value)
        rc = user32.QueryDisplayConfig(flags, ctypes.byref(pcount), paths, ctypes.byref(mcount), modes, None)
        if rc == 122:
            path_count, mode_count = pcount, mcount
            continue
        if rc: raise OSError(f"QueryDisplayConfig returned {rc}")
        result = []
        for path in paths[:pcount.value]:
            source_id = path.sourceInfo.id
            target_id = path.targetInfo.id
            luid = _luid(path.targetInfo.adapterId)
            target = _TARGET_NAME(); target.header = _HEADER(2, ctypes.sizeof(_TARGET_NAME), path.targetInfo.adapterId, target_id)
            target_rc = user32.DisplayConfigGetDeviceInfo(ctypes.byref(target.header))
            source = _SOURCE_NAME(); source.header = _HEADER(1, ctypes.sizeof(_SOURCE_NAME), path.sourceInfo.adapterId, source_id)
            source_rc = user32.DisplayConfigGetDeviceInfo(ctypes.byref(source.header))
            adapter = _ADAPTER_NAME(); adapter.header = _HEADER(4, ctypes.sizeof(_ADAPTER_NAME), path.targetInfo.adapterId, 0)
            adapter_rc = user32.DisplayConfigGetDeviceInfo(ctypes.byref(adapter.header))
            pos = None
            mode_index = path.sourceInfo.modeInfoIdx
            if mode_index != 0xFFFFFFFF and mode_index < mcount.value and modes[mode_index].infoType == 1:
                sm = modes[mode_index].mode.sourceMode
                pos = {"x": int(sm.positionX), "y": int(sm.positionY)}
            friendly = target.monitorFriendlyDeviceName if target_rc == 0 else None
            gdi = source.viewGdiDeviceName if source_rc == 0 else None
            result.append({"path_id": f"{luid['high']}:{luid['low']}:{target_id}", "output_technology": int(path.targetInfo.outputTechnology),
                "connector_instance": int(target.connectorInstance) if target_rc == 0 else None, "source_name": gdi or None,
                "target_name": friendly or None, "monitor_device_path": target.monitorDevicePath if target_rc == 0 else None,
                "edid_manufacturer_id": int(target.edidManufactureId) if target_rc == 0 else None,
                "edid_product_code_id": int(target.edidProductCodeId) if target_rc == 0 else None,
                "adapter_luid": luid, "adapter_name": adapter.adapterDevicePath if adapter_rc == 0 else None,
                "source_position": pos, "primary": bool(pos and pos["x"] == 0 and pos["y"] == 0),
                "properties": {"target_id": int(target_id), "source_id": int(source_id)},
                "errors": ([f"target info error {target_rc}"] if target_rc else []) + ([f"source info error {source_rc}"] if source_rc else []) + ([f"adapter info error {adapter_rc}"] if adapter_rc else [])})
        return result, []
    raise OSError("QueryDisplayConfig buffers changed repeatedly")

def collect_display_paths(api=None):
    if api is not None:
        try:
            return api.query_active_paths()
        except Exception as exc:
            return [], [{"scope": "DisplayConfig", "property": "QueryDisplayConfig", "error": f"{type(exc).__name__}: {exc}"}]
    if sys.platform != "win32":
        return [], [{"scope": "DisplayConfig", "property": "QueryDisplayConfig", "error": "僅支援 Windows"}]
    try:
        from ..models import DisplayPathRecord
        raw, errors = _query_windows()
        return [DisplayPathRecord(**row) for row in raw], errors
    except Exception as exc:
        return [], [{"scope": "DisplayConfig", "property": "QueryDisplayConfig", "error": f"{type(exc).__name__}: {exc}"}]

def match_sink_to_display(sink, paths):
    if not sink or not paths:
        return {"matched": False, "method": "none", "candidate_ids": [], "evidence": "缺少 sink 或 DisplayConfig 路徑"}
    target = sink.get("target_id")
    candidates = [p.path_id for p in paths if sink.get("adapter_luid") is not None and p.adapter_luid == sink.get("adapter_luid") and (target is None or p.properties.get("target_id") == target)]
    if len(candidates) == 1:
        return {"matched": True, "method": "adapter_luid_target" if target is not None else "adapter_luid", "candidate_ids": candidates, "evidence": "adapter LUID／target ID 唯一相符"}
    if candidates:
        return {"matched": False, "method": "adapter_luid", "candidate_ids": candidates, "evidence": "同一 adapter 上有多條路徑，保留歧義"}
    manufacturer, product = sink.get("manufacturer_id"), sink.get("product_id")
    if manufacturer is not None and product is not None:
        edid = [p.path_id for p in paths if p.edid_manufacturer_id == manufacturer and p.edid_product_code_id == product]
        if len(edid) == 1:
            return {"matched": True, "method": "edid", "candidate_ids": edid, "evidence": "EDID 廠商與產品 ID 唯一相符"}
        return {"matched": False, "method": "edid", "candidate_ids": edid, "evidence": "EDID 無匹配或重複"}
    return {"matched": False, "method": "none", "candidate_ids": [], "evidence": "無可用匹配欄位"}

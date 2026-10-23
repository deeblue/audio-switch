"""Core Audio endpoint collection. This module deliberately has no setters."""
from __future__ import annotations

import sys
import warnings
from typing import Any

from ..models import EndpointRecord

_PROPERTY_KEYS = {
    "friendly_name": "{A45C254E-DF1C-4EFD-8020-67D146A850E0} 14",
    "form_factor": "{1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E} 0",
    "container_id": "{8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C} 2",
    "stable_id": "{DFF21CE1-F70F-11D0-B917-00A0C9223196} 100",
}


def collect_audio_endpoints(enumerator: Any | None = None) -> tuple[list[EndpointRecord], list[dict[str, str]]]:
    """Collect active endpoints; an endpoint/property error is isolated."""
    errors: list[dict[str, str]] = []
    if sys.platform != "win32" and enumerator is None:
        return [], [{"scope": "audio", "property": "enumeration", "error": "Windows Core Audio is unavailable"}]
    if enumerator is None:
        try:
            from pycaw.pycaw import AudioUtilities, EDataFlow, ERole
            with warnings.catch_warnings(record=True) as captured:
                warnings.simplefilter("always")
                devices = AudioUtilities.GetAllDevices()
            errors.extend({"scope": "audio", "property": "property_store", "error": str(w.message)} for w in captured)
            enumerator = (devices, AudioUtilities.GetDeviceEnumerator(), EDataFlow, ERole)
        except Exception as exc:
            return [], [{"scope": "audio", "property": "enumeration", "error": f"{type(exc).__name__}: {exc}"}]
    if isinstance(enumerator, tuple):
        devices, dev_enum, data_flow, role = enumerator
    else:
        devices, dev_enum, data_flow, role = enumerator, None, None, None
    result: list[EndpointRecord] = []
    for device in devices:
        endpoint_id = str(getattr(device, "id", ""))
        state = str(getattr(device, "state", "unknown"))
        # pycaw's AudioDeviceState enum exposes ACTIVE by its numeric value 1.
        if getattr(getattr(device, "state", None), "value", getattr(device, "state", None)) not in (1, "active", "ACTIVE"):
            continue
        try:
            flow = "unknown"
            if dev_enum is not None:
                flow_value = dev_enum.GetDevice(endpoint_id).QueryInterface(__import__("pycaw.pycaw", fromlist=["IMMEndpoint"]).IMMEndpoint).GetDataFlow()
                flow = "render" if flow_value == 0 else "capture" if flow_value == 1 else str(flow_value)
            props = getattr(device, "properties", {}) or {}
            vals = {key: props.get(prop) for key, prop in _PROPERTY_KEYS.items()}
            rec = EndpointRecord(endpoint_id, flow, state, vals["friendly_name"],
                                 vals["form_factor"] if isinstance(vals["form_factor"], int) else None,
                                 str(vals["container_id"]) if vals["container_id"] is not None else None,
                                 str(vals["stable_id"]) if vals["stable_id"] is not None else None,
                                 properties={k: v for k, v in vals.items() if v is not None})
            for key, value in vals.items():
                if value is None:
                    rec.errors.append({"property": key, "error": "屬性不存在或未由 Core Audio property store 提供"})
            if dev_enum is not None:
                for role_name, role_value in (("console", role.eConsole.value), ("multimedia", role.eMultimedia.value), ("communications", role.eCommunications.value)):
                    try:
                        default = dev_enum.GetDefaultAudioEndpoint(0 if flow == "render" else 1, role_value)
                        rec.defaults[role_name] = default.GetId() == endpoint_id
                    except Exception as exc:
                        rec.errors.append({"property": f"default_{role_name}", "error": f"{type(exc).__name__}: {exc}"})
            result.append(rec)
        except Exception as exc:
            errors.append({"scope": endpoint_id or "endpoint", "property": "record", "error": f"{type(exc).__name__}: {exc}"})
    return result, errors

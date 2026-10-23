"""Serializable records used by the read-only audio topology probe."""
from __future__ import annotations

import base64
from dataclasses import asdict, dataclass, field, is_dataclass
from enum import Enum
from typing import Any


@dataclass
class EndpointRecord:
    endpoint_id: str
    flow: str
    state: str
    friendly_name: str | None = None
    form_factor: int | None = None
    container_id: str | None = None
    stable_id: str | None = None
    defaults: dict[str, bool] = field(default_factory=dict)
    devnode_id: str | None = None
    resolution_method: str = "unresolved"
    raw: bytes | None = None
    properties: dict[str, Any] = field(default_factory=dict)
    errors: list[dict[str, str]] = field(default_factory=list)


@dataclass
class DeviceNodeRecord:
    instance_id: str
    parent_id: str | None = None
    name: str | None = None
    properties: dict[str, Any] = field(default_factory=dict)
    location_paths: list[str] = field(default_factory=list)
    container_id: str | None = None
    errors: list[dict[str, str]] = field(default_factory=list)


@dataclass
class JackRecord:
    endpoint_id: str
    jack: dict[str, Any] = field(default_factory=dict)
    capabilities: int | None = None
    sink: dict[str, Any] | None = None
    errors: list[dict[str, str]] = field(default_factory=list)


@dataclass
class DisplayPathRecord:
    path_id: str
    output_technology: int | None = None
    connector_instance: int | None = None
    source_name: str | None = None
    target_name: str | None = None
    monitor_device_path: str | None = None
    edid_manufacturer_id: int | None = None
    edid_product_code_id: int | None = None
    adapter_luid: dict[str, int] | None = None
    adapter_name: str | None = None
    source_position: dict[str, int] | None = None
    primary: bool | None = None
    properties: dict[str, Any] = field(default_factory=dict)
    errors: list[dict[str, str]] = field(default_factory=list)


@dataclass
class MachineRecord:
    manufacturer: str | None = None
    model: str | None = None
    mtm: str | None = None


@dataclass
class SnapshotRecord:
    schema_version: int
    captured_at: str
    machine: MachineRecord
    endpoints: list[EndpointRecord] = field(default_factory=list)
    devnodes: list[DeviceNodeRecord] = field(default_factory=list)
    display_paths: list[DisplayPathRecord] = field(default_factory=list)
    errors: list[dict[str, str]] = field(default_factory=list)
    jacks: list[JackRecord] = field(default_factory=list)


def to_jsonable(value: object) -> object:
    """Convert records and Windows-native values into stable JSON values."""
    if is_dataclass(value) and not isinstance(value, type):
        return {key: to_jsonable(item) for key, item in asdict(value).items()}
    if isinstance(value, Enum):
        return to_jsonable(value.value)
    if isinstance(value, bytes):
        return base64.b64encode(value).decode("ascii")
    if isinstance(value, dict):
        return {str(key): to_jsonable(item) for key, item in sorted(value.items(), key=lambda pair: str(pair[0]))}
    if isinstance(value, set):
        return [to_jsonable(item) for item in sorted(value, key=repr)]
    if isinstance(value, (list, tuple)):
        return [to_jsonable(item) for item in value]
    if hasattr(value, "HighPart") and hasattr(value, "LowPart"):
        return {"high": int(value.HighPart), "low": int(value.LowPart)}
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
    return str(value)

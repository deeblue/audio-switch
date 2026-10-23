"""Read-only Core Audio device-topology and KS jack discovery."""
from __future__ import annotations

import ctypes
import sys
from ctypes import POINTER, c_uint, c_void_p
from ctypes.wintypes import BOOL, DWORD, LPWSTR, UINT, WORD

from ..models import JackRecord

try:
    from comtypes import COMMETHOD, CLSCTX_ALL, CLSCTX_INPROC_SERVER, GUID, HRESULT, IUnknown

    class IConnector(IUnknown):
        _iid_ = GUID("{9C2C4058-23F5-41DE-877A-DF3AF236A09E}")

    class IPart(IUnknown):
        _iid_ = GUID("{AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9}")

    class IDeviceTopology(IUnknown):
        _iid_ = GUID("{2A07407E-6497-4A18-9787-32F79BD0D98F}")
        _methods_ = (
            COMMETHOD([], HRESULT, "GetConnectorCount", (['out', 'retval'], POINTER(UINT), "count")),
            COMMETHOD([], HRESULT, "GetConnector", (['in'], UINT, "index"), (['out', 'retval'], POINTER(POINTER(IConnector)), "connector")),
            COMMETHOD([], HRESULT, "GetSubunitCount", (['out', 'retval'], POINTER(UINT), "count")),
            COMMETHOD([], HRESULT, "GetSubunit", (['in'], UINT, "index"), (['out', 'retval'], POINTER(c_void_p), "subunit")),
            COMMETHOD([], HRESULT, "GetPartById", (['in'], UINT, "local_id"), (['out', 'retval'], POINTER(POINTER(IPart)), "part")),
            COMMETHOD([], HRESULT, "GetDeviceId", (['out', 'retval'], POINTER(LPWSTR), "device_id")),
            COMMETHOD([], HRESULT, "GetSignalPath", (['in'], POINTER(IPart), "from_part"),
                      (['in'], POINTER(IPart), "to_part"), (['in'], BOOL, "reject_mixed_devices"),
                      (['out', 'retval'], POINTER(c_void_p), "parts")),
        )

    IConnector._methods_ = (
        COMMETHOD([], HRESULT, "GetType", (['out', 'retval'], POINTER(DWORD), "connector_type")),
        COMMETHOD([], HRESULT, "GetDataFlow", (['out', 'retval'], POINTER(DWORD), "data_flow")),
        COMMETHOD([], HRESULT, "ConnectTo", (['in'], POINTER(IConnector), "connector")),
        COMMETHOD([], HRESULT, "Disconnect"),
        COMMETHOD([], HRESULT, "IsConnected", (['out', 'retval'], POINTER(BOOL), "connected")),
        COMMETHOD([], HRESULT, "GetConnectedTo", (['out', 'retval'], POINTER(POINTER(IConnector)), "connector")),
        COMMETHOD([], HRESULT, "GetConnectorIdConnectedTo", (['out', 'retval'], POINTER(LPWSTR), "connector_id")),
        COMMETHOD([], HRESULT, "GetDeviceIdConnectedTo", (['out', 'retval'], POINTER(LPWSTR), "device_id")),
    )

    IPart._methods_ = (
        COMMETHOD([], HRESULT, "GetName", (['out', 'retval'], POINTER(LPWSTR), "name")),
        COMMETHOD([], HRESULT, "GetLocalId", (['out', 'retval'], POINTER(UINT), "local_id")),
        COMMETHOD([], HRESULT, "GetGlobalId", (['out', 'retval'], POINTER(LPWSTR), "global_id")),
        COMMETHOD([], HRESULT, "GetPartType", (['out', 'retval'], POINTER(DWORD), "part_type")),
        COMMETHOD([], HRESULT, "GetSubType", (['out', 'retval'], POINTER(GUID), "sub_type")),
        COMMETHOD([], HRESULT, "GetControlInterfaceCount", (['out', 'retval'], POINTER(UINT), "count")),
        COMMETHOD([], HRESULT, "GetControlInterface", (['in'], UINT, "index"), (['out', 'retval'], POINTER(c_void_p), "control_interface")),
        COMMETHOD([], HRESULT, "EnumPartsIncoming", (['out', 'retval'], POINTER(c_void_p), "parts")),
        COMMETHOD([], HRESULT, "EnumPartsOutgoing", (['out', 'retval'], POINTER(c_void_p), "parts")),
        COMMETHOD([], HRESULT, "GetTopologyObject", (['out', 'retval'], POINTER(POINTER(IDeviceTopology)), "topology")),
        COMMETHOD([], HRESULT, "Activate", (['in'], DWORD, "class_context"), (['in'], POINTER(GUID), "iid"),
                  (['out', 'retval'], POINTER(POINTER(IUnknown)), "interface")),
        COMMETHOD([], HRESULT, "RegisterControlChangeCallback", (['in'], POINTER(GUID), "iid"), (['in'], c_void_p, "callback")),
        COMMETHOD([], HRESULT, "UnregisterControlChangeCallback", (['in'], c_void_p, "callback")),
    )

    _IID_IKS_JACK_DESCRIPTION = GUID("{4509F757-2D46-4637-8E62-CE7DB944F57B}")
    _IID_IKS_JACK_DESCRIPTION2 = GUID("{478F3A9B-E0C9-4827-9228-6F5505FFE76A}")
    _IID_IKS_JACK_SINK_INFORMATION = GUID("{D9BD72ED-290F-4581-9FF3-61027A8FE532}")

    class KSJACK_DESCRIPTION(ctypes.Structure):
        _fields_ = [("ChannelMapping", DWORD), ("Color", DWORD), ("ConnectionType", DWORD),
                    ("GeoLocation", DWORD), ("GenLocation", DWORD), ("PortConnection", DWORD),
                    ("IsConnected", BOOL)]

    class KSJACK_DESCRIPTION2(ctypes.Structure):
        _fields_ = [("DeviceStateInfo", DWORD), ("JackCapabilities", DWORD)]

    class _LUID(ctypes.Structure):
        _fields_ = [("LowPart", DWORD), ("HighPart", ctypes.c_long)]

    class KSJACK_SINK_INFORMATION(ctypes.Structure):
        _fields_ = [("ConnType", DWORD), ("ManufacturerId", WORD), ("ProductId", WORD),
                    ("AudioLatency", WORD), ("HDCPCapable", BOOL), ("AICapable", BOOL),
                    ("SinkDescriptionLength", ctypes.c_ubyte), ("SinkDescription", ctypes.c_wchar * 32),
                    ("PortId", _LUID)]

    class IKsJackDescription(IUnknown):
        _iid_ = _IID_IKS_JACK_DESCRIPTION
        _methods_ = (
            COMMETHOD([], HRESULT, "GetJackCount", (['out', 'retval'], POINTER(UINT), "count")),
            COMMETHOD([], HRESULT, "GetJackDescription", (['in'], UINT, "index"), (['out'], POINTER(KSJACK_DESCRIPTION), "description")),
        )

    class IKsJackDescription2(IUnknown):
        _iid_ = _IID_IKS_JACK_DESCRIPTION2
        _methods_ = (
            COMMETHOD([], HRESULT, "GetJackCount", (['out', 'retval'], POINTER(UINT), "count")),
            COMMETHOD([], HRESULT, "GetJackDescription2", (['in'], UINT, "index"), (['out'], POINTER(KSJACK_DESCRIPTION2), "description")),
        )

    class IKsJackSinkInformation(IUnknown):
        _iid_ = _IID_IKS_JACK_SINK_INFORMATION
        _methods_ = (
            COMMETHOD([], HRESULT, "GetJackSinkInformation", (['out'], POINTER(KSJACK_SINK_INFORMATION), "information")),
        )
except ImportError:  # Keep fixtures importable when comtypes is not installed.
    COMMETHOD = CLSCTX_ALL = CLSCTX_INPROC_SERVER = GUID = HRESULT = IUnknown = None
    IConnector = IPart = IDeviceTopology = IKsJackDescription = IKsJackDescription2 = IKsJackSinkInformation = None
    _IID_IKS_JACK_DESCRIPTION = _IID_IKS_JACK_DESCRIPTION2 = _IID_IKS_JACK_SINK_INFORMATION = None


def _get_field(obj, *names):
    for name in names:
        if hasattr(obj, name):
            return getattr(obj, name)
    raise AttributeError(f"missing field {names[0]}")


def _read_jack_description(iface, index):
    # The description is declared as an out parameter, so comtypes allocates and returns it.
    description = iface.GetJackDescription(index)
    return {
        "index": index,
        "channel_mapping": int(description.ChannelMapping),
        "color": int(description.Color),
        "connection_type": int(description.ConnectionType),
        "geo_location": int(description.GeoLocation),
        "general_location": int(description.GenLocation),
        "port_connection": int(description.PortConnection),
        "is_connected": bool(description.IsConnected),
    }


def _read_jack_description2(iface, index):
    description = iface.GetJackDescription2(index)
    return {"index": index, "device_state_info": int(description.DeviceStateInfo),
            "jack_capabilities": int(description.JackCapabilities)}


def _read_sink(iface):
    sink = iface.GetJackSinkInformation()
    text = sink.SinkDescription.split("\0", 1)[0]
    return {"connection_type": int(sink.ConnType), "manufacturer_id": int(sink.ManufacturerId),
            "product_id": int(sink.ProductId), "audio_latency": int(sink.AudioLatency),
            "hdcp_capable": bool(sink.HDCPCapable), "ai_capable": bool(sink.AICapable),
            "sink_description_length": int(sink.SinkDescriptionLength), "sink_description": text,
            "port_id": {"high": int(sink.PortId.HighPart), "low": int(sink.PortId.LowPart)}}


def collect_jacks_from_part(endpoint_id: str, part, device_interface_resolver=None):
    """Read supported jack and sink interfaces from the adapter-side connector."""
    errors = []
    adapter_device_id = None
    adapter_instance_id = None
    try:
        adapter_device_id = str(part.GetTopologyObject().GetDeviceId())
    except Exception as exc:
        errors.append({"scope": endpoint_id, "property": "topology_device_id", "error": f"{type(exc).__name__}: {exc}"})
    if adapter_device_id and device_interface_resolver:
        try:
            adapter_instance_id = device_interface_resolver(adapter_device_id)
        except Exception as exc:
            errors.append({"scope": endpoint_id, "property": "topology_instance_id", "error": f"{type(exc).__name__}: {exc}"})

    activated = {}
    for name, iid, interface_type in (("IKsJackDescription", _IID_IKS_JACK_DESCRIPTION, IKsJackDescription),
                                      ("IKsJackDescription2", _IID_IKS_JACK_DESCRIPTION2, IKsJackDescription2),
                                      ("IKsJackSinkInformation", _IID_IKS_JACK_SINK_INFORMATION, IKsJackSinkInformation)):
        try:
            result = part.Activate(CLSCTX_INPROC_SERVER, iid)
            activated[name] = result.QueryInterface(interface_type) if interface_type and hasattr(result, "QueryInterface") else result
        except Exception as exc:
            errors.append({"scope": endpoint_id, "property": name, "error": f"{type(exc).__name__}: {exc}"})

    jack_iface = activated.get("IKsJackDescription")
    jack2_iface = activated.get("IKsJackDescription2")
    sink_iface = activated.get("IKsJackSinkInformation")
    jack_count = jack2_count = 0
    if jack_iface:
        try:
            jack_count = int(jack_iface.GetJackCount())
        except Exception as exc:
            errors.append({"scope": endpoint_id, "property": "jack_count", "error": f"{type(exc).__name__}: {exc}"})
    if jack2_iface:
        try:
            jack2_count = int(jack2_iface.GetJackCount())
        except Exception as exc:
            errors.append({"scope": endpoint_id, "property": "jack2_count", "error": f"{type(exc).__name__}: {exc}"})
    sink = None
    if sink_iface:
        try:
            sink = _read_sink(sink_iface)
        except Exception as exc:
            errors.append({"scope": endpoint_id, "property": "sink_information", "error": f"{type(exc).__name__}: {exc}"})

    count = max(jack_count, jack2_count, 1 if sink else 0)
    records = []
    for index in range(count):
        jack = {}
        capabilities = None
        record_errors = []
        if jack_iface and index < jack_count:
            try:
                jack = _read_jack_description(jack_iface, index)
            except Exception as exc:
                error = {"property": f"jack_{index}", "error": f"{type(exc).__name__}: {exc}"}
                errors.append({"scope": endpoint_id, **error}); record_errors.append(error)
        if jack2_iface and index < jack2_count:
            try:
                capabilities = _read_jack_description2(jack2_iface, index)["jack_capabilities"]
            except Exception as exc:
                error = {"property": f"jack2_{index}", "error": f"{type(exc).__name__}: {exc}"}
                errors.append({"scope": endpoint_id, **error}); record_errors.append(error)
        records.append(JackRecord(endpoint_id, jack, capabilities, sink if index == 0 else None,
                                  adapter_device_id, adapter_instance_id, record_errors))
    return records, errors


def _default_endpoint_part(endpoint_id: str):
    from pycaw.pycaw import AudioUtilities
    import comtypes
    device = AudioUtilities.GetDeviceEnumerator().GetDevice(endpoint_id)
    topology = device.Activate(IDeviceTopology._iid_, comtypes.CLSCTX_ALL, None).QueryInterface(IDeviceTopology)
    endpoint_connector = topology.GetConnector(0)
    adapter_connector = endpoint_connector.GetConnectedTo()
    return adapter_connector.QueryInterface(IPart)


def collect_jacks(endpoint_id: str, part_provider=None, device_interface_resolver=None):
    if sys.platform != "win32" and part_provider is None:
        return [], [{"scope": endpoint_id, "property": "device_topology", "error": "僅支援 Windows"}]
    try:
        part = part_provider(endpoint_id) if part_provider else _default_endpoint_part(endpoint_id)
    except Exception as exc:
        return [], [{"scope": endpoint_id, "property": "device_topology", "error": f"{type(exc).__name__}: {exc}"}]
    return collect_jacks_from_part(endpoint_id, part, device_interface_resolver)


def collect_jack_sink(endpoint_id: str, **kwargs):
    records, errors = collect_jacks(endpoint_id, **kwargs)
    sinks = [record.sink for record in records if record.sink]
    if not sinks:
        return None, errors
    if len(sinks) == 1:
        return sinks[0], errors
    return {"ambiguous": True, "candidates": sinks}, errors

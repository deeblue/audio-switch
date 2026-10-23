import sys
import unittest
from probes.audio_topology.native.audio_topology import (
    KSJACK_DESCRIPTION,
    KSJACK_DESCRIPTION2,
    KSJACK_SINK_INFORMATION,
    _IID_IKS_JACK_DESCRIPTION,
    _IID_IKS_JACK_DESCRIPTION2,
    _IID_IKS_JACK_SINK_INFORMATION,
    collect_jacks,
    collect_jacks_from_part,
)


class FakeTopology:
    def GetDeviceId(self):
        return r"\\?\HDAUDIO#FUNC_01&VEN_10EC&DEV_0257"


class FakePart:
    def __init__(self, interfaces):
        self.interfaces = interfaces

    def GetTopologyObject(self):
        return FakeTopology()

    def Activate(self, clsctx, iid):
        if str(iid) in self.interfaces:
            return self.interfaces[str(iid)]
        raise OSError("E_NOINTERFACE")


class FakeJackDescription:
    def GetJackCount(self):
        return 1

    def GetJackDescription(self, index):
        description = KSJACK_DESCRIPTION()
        description.ConnectionType = 3
        description.GeoLocation = 1
        description.GenLocation = 1
        description.PortConnection = 1
        description.IsConnected = True
        description.Color = 0x00FF00
        return description


class FakeJackDescription2:
    def GetJackCount(self):
        return 1

    def GetJackDescription2(self, index):
        description = KSJACK_DESCRIPTION2()
        description.DeviceStateInfo = 0
        description.JackCapabilities = 1
        return description


class FakeSink:
    def GetJackSinkInformation(self):
        information = KSJACK_SINK_INFORMATION()
        information.ConnType = 1
        information.ManufacturerId = 0x10CC
        information.ProductId = 0xD0E6
        information.HDCPCapable = True
        information.SinkDescription = "S2719DGF"
        information.PortId.HighPart = 0
        information.PortId.LowPart = 7
        return information


class JackTests(unittest.TestCase):
    def test_collects_jacks_capabilities_sink_and_adapter_id(self):
        part = FakePart({
            str(_IID_IKS_JACK_DESCRIPTION): FakeJackDescription(),
            str(_IID_IKS_JACK_DESCRIPTION2): FakeJackDescription2(),
            str(_IID_IKS_JACK_SINK_INFORMATION): FakeSink(),
        })

        records, errors = collect_jacks_from_part("endpoint", part)

        self.assertEqual(errors, [])
        self.assertEqual(len(records), 1)
        self.assertEqual(records[0].jack["connection_type"], 3)
        self.assertTrue(records[0].jack["is_connected"])
        self.assertEqual(records[0].capabilities, 1)
        self.assertEqual(records[0].sink["sink_description"], "S2719DGF")
        self.assertEqual(records[0].sink["port_id"], {"high": 0, "low": 7})
        self.assertTrue(records[0].adapter_device_id.startswith(r"\\?\HDAUDIO#"))

    def test_unsupported_interfaces_are_local_errors(self):
        records, errors = collect_jacks_from_part("endpoint", FakePart({}))
        self.assertEqual(records, [])
        self.assertEqual({error["property"] for error in errors}, {
            "IKsJackDescription", "IKsJackDescription2", "IKsJackSinkInformation"
        })


@unittest.skipUnless(sys.platform == "win32", "requires Windows Core Audio")
class LiveJackTests(unittest.TestCase):
    def test_at_least_one_active_endpoint_reports_a_jack_description(self):
        # Guards against interface definitions that only work with fakes.
        from probes.audio_topology.native.core_audio import collect_audio_endpoints
        endpoints, _ = collect_audio_endpoints()
        if not endpoints:
            self.skipTest("no active audio endpoints")
        records = [record for endpoint in endpoints for record in collect_jacks(endpoint.endpoint_id)[0]]
        self.assertTrue(any(record.jack for record in records))

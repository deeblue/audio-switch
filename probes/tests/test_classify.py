import unittest
from probes.audio_topology.classify import classify_endpoint, build_tree
from probes.audio_topology.calibration import PortMap
from probes.audio_topology.models import EndpointRecord, DeviceNodeRecord, SnapshotRecord, MachineRecord

class ClassifyTests(unittest.TestCase):
    def test_usb_endpoint_requires_a_calibrated_location_path(self):
        endpoint = EndpointRecord("same-id", "render", "active", "same name")
        node = DeviceNodeRecord("USB\\VID_1234&PID_0001", location_paths=["PCIROOT#USB(2)"])
        pm = PortMap({"interfaces": [{"id":"usb1", "usb_location_path_prefixes": ["PCIROOT#USB(2)"]}]})
        result = classify_endpoint(endpoint, [node], [], None, [], pm)
        self.assertEqual(result.category, "A")
        self.assertEqual(result.interface_id, "usb1")

    def test_unknown_endpoints_stay_unique_and_visible(self):
        pm = PortMap({"interfaces": []})
        snapshot = SnapshotRecord(1, "now", MachineRecord(), [
            EndpointRecord("same-1", "render", "active", "Realtek"), EndpointRecord("same-2", "render", "active", "Realtek")])
        tree = build_tree(snapshot, pm)
        unknown = next(x for x in tree["roots"] if x["id"] == "未辨識")
        self.assertEqual([x["endpoint_id"] for x in unknown["children"]], ["same-1", "same-2"])

    def test_tree_walks_case_insensitive_pnp_chain_and_short_guid(self):
        endpoint = EndpointRecord("{0.0.0.00000000}.{12345678-ABCD}", "render", "active", "USB audio", devnode_id="swd\\mmdevapi\\ep")
        nodes = [DeviceNodeRecord("SWD\\MMDEVAPI\\EP", parent_id="USB\\VID_1"), DeviceNodeRecord("USB\\VID_1")]
        tree = build_tree(SnapshotRecord(1, "now", MachineRecord(), [endpoint], nodes), PortMap({"interfaces":[]}))
        unknown = next(x for x in tree["roots"] if x["id"] == "未辨識")
        self.assertEqual(unknown["children"][0]["category"], "A")
        self.assertEqual(unknown["children"][0]["short_id"], "12345678")

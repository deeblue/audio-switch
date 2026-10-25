import unittest
from probes.audio_topology.classify import classify_endpoint, build_tree
from probes.audio_topology.calibration import PortMap
from probes.audio_topology.models import EndpointRecord, DeviceNodeRecord, SnapshotRecord, MachineRecord, JackRecord

class ClassifyTests(unittest.TestCase):
    def test_tree_ignores_inactive_endpoint_states(self):
        active = EndpointRecord("active-ep", "render", "AudioDeviceState.Active", "Active speaker")
        inactive = EndpointRecord("inactive-ep", "render", "AudioDeviceState.Unplugged", "Unplugged speaker")
        snapshot = SnapshotRecord(1, "now", MachineRecord(), [active, inactive])
        tree = build_tree(snapshot, PortMap({"interfaces": []}))
        names = [node["name"] for root in tree["roots"] for node in root["children"]]
        self.assertIn("Active speaker", names)
        self.assertNotIn("Unplugged speaker", names)

    def test_endpoint_swd_node_does_not_hide_integrated_audio_classification(self):
        endpoint = EndpointRecord("ep", "render", "active")
        nodes = [DeviceNodeRecord(r"SWD\MMDEVAPI\ep"), DeviceNodeRecord(r"INTELAUDIO\FUNC_01")]
        result = classify_endpoint(endpoint, nodes, [], None, [], PortMap({"interfaces": []}))
        self.assertEqual(result.category, "C")

    def test_jack_sink_classifies_display_audio_as_category_b(self):
        endpoint = EndpointRecord("ep", "render", "active")
        nodes = [DeviceNodeRecord(r"SWD\MMDEVAPI\ep"), DeviceNodeRecord(r"HDAUDIO\FUNC_01")]
        result = classify_endpoint(endpoint, nodes, [], {"matched": True, "method": "edid", "interface_id": "hdmi"}, [], PortMap({"interfaces": []}))
        self.assertEqual(result.category, "B")
        self.assertEqual(result.interface_id, "hdmi")

    def test_jack_description_or_audio_adapter_classifies_category_c(self):
        endpoint = EndpointRecord("ep", "capture", "active")
        node = DeviceNodeRecord(r"SWD\MMDEVAPI\ep")
        result = classify_endpoint(endpoint, [node], [JackRecord("ep", jack={"connection_type": 3})], None, [], PortMap({"interfaces": []}))
        self.assertEqual(result.category, "C")

    def test_usb_endpoint_requires_a_calibrated_location_path(self):
        endpoint = EndpointRecord("same-id", "render", "active", "same name")
        node = DeviceNodeRecord("USB\\VID_1234&PID_0001", location_paths=["PCIROOT#USB(2)"])
        pm = PortMap({"interfaces": [{"id":"usb1", "usb_location_path_prefixes": ["PCIROOT#USB(2)"]}]})
        result = classify_endpoint(endpoint, [node], [], None, [], pm)
        self.assertEqual(result.category, "A")
        self.assertEqual(result.interface_id, "usb1")

    def test_multiple_hs_and_ss_prefixes_map_to_one_physical_interface(self):
        endpoint = EndpointRecord("ep", "render", "active")
        node = DeviceNodeRecord("USB\\VID_303A&PID_2986", location_paths=["ACPI#HS07", "ACPI#SS07"])
        pm = PortMap({"interfaces": [{"id": "usb-c-1", "usb_location_path_prefixes": ["ACPI#HS07", "ACPI#SS07"]}]})
        result = classify_endpoint(endpoint, [node], [], None, [], pm)
        self.assertEqual(result.category, "A")
        self.assertEqual(result.interface_id, "usb-c-1")

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
        usb_device = unknown["children"][0]
        self.assertEqual(usb_device["kind"], "device")
        self.assertEqual(usb_device["children"][0]["category"], "A")
        self.assertEqual(usb_device["children"][0]["short_id"], "12345678")

    def test_usb_audio_and_display_are_grouped_under_same_usb_device(self):
        endpoint = EndpointRecord("ep", "render", "active", "Speakers (speaker)", devnode_id="SWD\\MMDEVAPI\\ep")
        nodes = [DeviceNodeRecord("SWD\\MMDEVAPI\\ep", parent_id="USB\\VID_303A&PID_2986"),
                 DeviceNodeRecord("USB\\VID_303A&PID_2986", name="speaker", location_paths=["PCIROOT#USB(7)"])]
        from probes.audio_topology.models import DisplayPathRecord
        display = DisplayPathRecord("volatile", target_name="S2719DGF", adapter_name=r"\\?\USB#VID_303A&PID_2986", properties={"target_id": 256})
        tree = build_tree(SnapshotRecord(1, "now", MachineRecord(), [endpoint], nodes, [display]), PortMap({"interfaces": []}))
        unknown = next(x for x in tree["roots"] if x["id"] == "未辨識")
        self.assertEqual(unknown["children"][0]["name"], "speaker")
        self.assertEqual(unknown["children"][0]["children"][1]["kind"], "display")

class HostJackAndUsbDisplayTests(unittest.TestCase):
    def _tree(self, endpoints, nodes, jacks=(), displays=()):
        pm = PortMap({"interfaces": [{"id": "jack", "name": "3.5mm", "type": "audio-jack"}]})
        return build_tree(SnapshotRecord(1, "now", MachineRecord(), endpoints, nodes, list(displays), [], list(jacks)), pm)

    def test_only_physical_jacks_map_to_host_audio_jack(self):
        # Shapes observed on the T14p: the integrated speaker also reports IsConnected.
        endpoints = [EndpointRecord("hp", "render", "active", "2nd output", devnode_id="INTELAUDIO\FUNC_01"),
                     EndpointRecord("spk", "render", "active", "speaker", devnode_id="INTELAUDIO\FUNC_01")]
        jacks = [JackRecord("hp", jack={"connection_type": 1, "port_connection": 0, "is_connected": True}),
                 JackRecord("spk", jack={"connection_type": 7, "port_connection": 1, "is_connected": True})]
        tree = self._tree(endpoints, [DeviceNodeRecord("INTELAUDIO\FUNC_01")], jacks)
        roots = {root["id"]: [child["endpoint_id"] for child in root["children"]] for root in tree["roots"]}
        self.assertEqual(roots["jack"], ["hp"])
        self.assertEqual(roots["內建"], ["spk"])

    def test_usb_display_attaches_by_interface_instance_when_parent_also_matches(self):
        endpoint = EndpointRecord("ep", "render", "active", "Speakers (speaker)", devnode_id="USB\VID_303A&PID_2986&MI_03\X")
        nodes = [DeviceNodeRecord("USB\VID_303A&PID_2986&MI_03\X", parent_id="USB\VID_303A&PID_2986\SN"),
                 DeviceNodeRecord("USB\VID_303A&PID_2986&MI_00\Y", parent_id="USB\VID_303A&PID_2986\SN"),
                 DeviceNodeRecord("USB\VID_303A&PID_2986\SN", name="speaker", location_paths=["PCIROOT#USB(5)"])]
        from probes.audio_topology.models import DisplayPathRecord
        display = DisplayPathRecord("volatile", target_name="S2719DGF", adapter_instance_id="USB\VID_303A&PID_2986&MI_00\Y",
                                    adapter_name=r"\?\USB#VID_303A&PID_2986&MI_00#Y#{guid}", properties={"target_id": 256})
        tree = self._tree([endpoint], nodes, displays=[display])
        device = next(r for r in tree["roots"] if r["id"] == "未辨識")["children"][0]
        self.assertEqual([c.get("kind") for c in device["children"]], [None, "display"])

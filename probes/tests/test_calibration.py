import unittest
from probes.audio_topology.calibration import PortMap, calibrate_port_map, load_port_map
from probes.audio_topology.models import SnapshotRecord, MachineRecord, DeviceNodeRecord
from probes.audio_topology.models import DisplayPathRecord

class CalibrationTests(unittest.TestCase):
    def setUp(self):
        self.pm = PortMap({"machine": {"mtm": "21RU"}, "interfaces": [{"id": "c1", "usb_location_path_prefixes": []}]})
        self.before = SnapshotRecord(1, "a", MachineRecord(mtm="21RU"))

    def test_ambiguous_candidates_do_not_calibrate(self):
        after = SnapshotRecord(1, "b", MachineRecord(mtm="21RU"), devnodes=[DeviceNodeRecord("USB\\a", location_paths=["P#A"]), DeviceNodeRecord("USB\\b", location_paths=["P#B"])])
        result = calibrate_port_map("c1", self.before, after, self.pm)
        self.assertEqual(result["status"], "ambiguous")
        self.assertEqual(self.pm.data["interfaces"][0]["usb_location_path_prefixes"], [])

    def test_machine_mismatch_disables_calibration(self):
        pm = load_port_map(__import__("pathlib").Path(__file__).parents[1] / "ports.json", {"manufacturer":"other"})
        self.assertTrue(pm.warning)
        with self.assertRaises(ValueError): calibrate_port_map("usb-c-1", self.before, self.before, pm)

    def test_invalid_port_map_schema_is_rejected(self):
        with __import__("tempfile").TemporaryDirectory() as tmp:
            path = __import__("pathlib").Path(tmp) / "ports.json"
            path.write_text('{"schema_version": 2, "interfaces": []}', encoding="utf-8")
            with self.assertRaises(ValueError): load_port_map(path)

    def test_unique_display_path_is_calibrated(self):
        after = SnapshotRecord(1, "b", MachineRecord(mtm="21RU"), display_paths=[DisplayPathRecord("0:1:2", output_technology=5, connector_instance=3, source_name=r"\\.\DISPLAY9", adapter_luid={"high": 1, "low": 2}, adapter_name=r"\\?\DISPLAY#GPU", properties={"target_id":2})])
        result = calibrate_port_map("c1", self.before, after, self.pm)
        self.assertEqual(result["status"], "calibrated")
        self.assertEqual(self.pm.data["interfaces"][0]["display_connectors"][0]["connector_instance"], 3)
        connector = self.pm.data["interfaces"][0]["display_connectors"][0]
        self.assertEqual(connector, {"adapter_device_path": r"\\?\DISPLAY#GPU", "target_id": 2,
                                     "connector_instance": 3, "output_technology": 5})

    def test_display_connector_match_ignores_reenumeration_fields(self):
        from probes.audio_topology.calibration import display_connector_matches
        stored = {"adapter_device_path": r"\\?\DISPLAY#GPU", "target_id": 256,
                  "connector_instance": 0, "output_technology": 5}
        path = DisplayPathRecord("new-path-id", output_technology=5, connector_instance=0,
                                 source_name=r"\\.\DISPLAY42", adapter_luid={"high": 9, "low": 9},
                                 adapter_name=r"\\?\DISPLAY#GPU", properties={"target_id": 256})
        self.assertTrue(display_connector_matches(path, stored))

    def test_unique_usb_parent_calibrates_all_location_path_variants(self):
        parent = DeviceNodeRecord("USB\\VID_1234&PID_5678", location_paths=["PCIROOT#USB(2)", "ACPI#USB(2)"])
        mi = DeviceNodeRecord("USB\\VID_1234&PID_5678&MI_00", parent_id=parent.instance_id, location_paths=["PCIROOT#USB(2)#MI(0)"])
        after = SnapshotRecord(1, "b", MachineRecord(mtm="21RU"), devnodes=[parent, mi])
        result = calibrate_port_map("c1", self.before, after, self.pm)
        self.assertEqual(result["status"], "calibrated")
        self.assertEqual(self.pm.data["interfaces"][0]["usb_location_path_prefixes"], ["ACPI#USB(2)", "PCIROOT#USB(2)"])

    def test_unique_existing_usb_parent_location_change_calibrates(self):
        parent_before = DeviceNodeRecord("USB\\VID_1234&PID_5678", location_paths=["PCIROOT#USB(1)"])
        parent_after = DeviceNodeRecord("USB\\VID_1234&PID_5678", location_paths=["PCIROOT#USB(5)"])
        before = SnapshotRecord(1, "a", MachineRecord(mtm="21RU"), devnodes=[parent_before])
        after = SnapshotRecord(1, "b", MachineRecord(mtm="21RU"), devnodes=[parent_after])

        result = calibrate_port_map("c1", before, after, self.pm)

        self.assertEqual(result["status"], "calibrated")
        self.assertEqual(result["location_paths"], ["PCIROOT#USB(5)"])
        self.assertEqual(self.pm.data["interfaces"][0]["usb_location_path_prefixes"], ["PCIROOT#USB(5)"])

    def test_hs_and_ss_paths_are_both_retained_and_marked_by_transport(self):
        from probes.audio_topology.calibration import usb_path_type
        self.assertEqual(usb_path_type(["ACPI#HS07"]), "USB 2")
        self.assertEqual(usb_path_type(["ACPI#SS07"]), "USB 3")
        self.assertEqual(usb_path_type(["ACPI#HS07", "ACPI#SS07"]), "USB 3")

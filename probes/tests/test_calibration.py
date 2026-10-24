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

    def test_dock_attachments_on_each_transport_are_calibrated_together(self):
        hs_hub = DeviceNodeRecord("USB\VID_2109&PID_2822\1", parent_id="USB\ROOT_HUB30\PCH",
                                  location_paths=["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(1)", "ACPI(_SB_)#ACPI(PC00)#ACPI(XHCI)#ACPI(RHUB)#ACPI(HS01)"])
        ss_hub = DeviceNodeRecord("USB\VID_2109&PID_0822\1", parent_id="USB\ROOT_HUB30\TCSS",
                                  location_paths=["PCIROOT(0)#PCI(0D00)#USBROOT(0)#USB(1)", "ACPI(_SB_)#ACPI(PC00)#ACPI(TXHC)#ACPI(RHUB)#ACPI(SS01)"])
        bridge = DeviceNodeRecord("PCI\VEN_8086&DEV_1136\1", parent_id="PCI\ROOTPORT",
                                  location_paths=["PCIROOT(0)#PCI(0700)#PCI(0000)"])
        speaker = DeviceNodeRecord("USB\VID_0D8C&PID_0014\1", parent_id=hs_hub.instance_id,
                                   location_paths=["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(1)#USB(3)"])
        speaker_mi = DeviceNodeRecord("USB\VID_0D8C&PID_0014&MI_00\1", parent_id=speaker.instance_id,
                                      location_paths=["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(1)#USB(3)#USBMI(0)"])
        after = SnapshotRecord(1, "b", MachineRecord(mtm="21RU"), devnodes=[hs_hub, ss_hub, bridge, speaker, speaker_mi])

        result = calibrate_port_map("c1", self.before, after, self.pm)

        self.assertEqual(result["status"], "calibrated")
        self.assertEqual(sorted(result["transports"]), ["PCIe 隧道", "USB 2", "USB 3"])
        prefixes = self.pm.data["interfaces"][0]["usb_location_path_prefixes"]
        self.assertIn("PCIROOT(0)#PCI(0D00)#USBROOT(0)#USB(1)", prefixes)
        self.assertIn("PCIROOT(0)#PCI(0700)#PCI(0000)", prefixes)
        self.assertNotIn("PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(1)#USB(3)", prefixes)

    def test_two_attachments_on_same_transport_are_ambiguous(self):
        first = DeviceNodeRecord("USB\VID_1\1", parent_id="USB\ROOT", location_paths=["ACPI#HS01"])
        second = DeviceNodeRecord("USB\VID_2\1", parent_id="USB\ROOT", location_paths=["ACPI#HS05"])
        after = SnapshotRecord(1, "b", MachineRecord(mtm="21RU"), devnodes=[first, second])

        result = calibrate_port_map("c1", self.before, after, self.pm)

        self.assertEqual(result["status"], "ambiguous")
        self.assertEqual(self.pm.data["interfaces"][0]["usb_location_path_prefixes"], [])

    def test_property_only_change_is_not_relocation(self):
        root_before = DeviceNodeRecord("USB\ROOT_HUB30\1", location_paths=["PCIROOT#USBROOT(0)"])
        root_after = DeviceNodeRecord("USB\ROOT_HUB30\1", location_paths=["PCIROOT#USBROOT(0)"], properties={"removal_policy": 1})
        device_before = DeviceNodeRecord("USB\VID_1234&PID_5678\1", parent_id=root_before.instance_id, location_paths=["PCIROOT#USBROOT(0)#USB(7)"])
        device_after = DeviceNodeRecord("USB\VID_1234&PID_5678\1", parent_id=root_before.instance_id, location_paths=["PCIROOT#USBROOT(0)#USB(6)"])
        before = SnapshotRecord(1, "a", MachineRecord(mtm="21RU"), devnodes=[root_before, device_before])
        after = SnapshotRecord(1, "b", MachineRecord(mtm="21RU"), devnodes=[root_after, device_after])

        result = calibrate_port_map("c1", before, after, self.pm)

        self.assertEqual(result["status"], "calibrated")
        self.assertEqual(result["location_paths"], ["PCIROOT#USBROOT(0)#USB(6)"])

    def test_root_complex_pci_device_is_not_an_attachment(self):
        igpu = DeviceNodeRecord("PCI\VEN_8086&DEV_7D51\1", location_paths=["PCIROOT(0)#PCI(0200)", "ACPI(_SB_)#ACPI(PC00)#ACPI(GFX0)"])
        device = DeviceNodeRecord("USB\VID_1234&PID_5678\1", parent_id="USB\ROOT", location_paths=["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(6)", "ACPI(_SB_)#ACPI(PC00)#ACPI(XHCI)#ACPI(RHUB)#ACPI(HS06)"])
        after = SnapshotRecord(1, "b", MachineRecord(mtm="21RU"), devnodes=[igpu, device])

        result = calibrate_port_map("c1", self.before, after, self.pm)

        self.assertEqual(result["transports"], ["USB 2"])

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

    def test_dock_calibration_records_usb_path_and_screens_on_the_host_gpu(self):
        dock = DeviceNodeRecord("USB\VID_03F0&PID_3BAF\DOCK", parent_id="USB\ROOT_HUB30\1",
                                location_paths=["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(5)"])
        adapter = DeviceNodeRecord("USB\VID_34C7&PID_2114\ADAPTER", parent_id=dock.instance_id,
                                   location_paths=["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(5)#USB(4)"])
        after = SnapshotRecord(1, "b", MachineRecord(mtm="21RU"), devnodes=[dock, adapter],
                               display_paths=[_gpu_path("hdmi-screen", 8261, 0), _gpu_path("dp-screen", 12613, 1),
                                              DisplayPathRecord("usb-screen", output_technology=5, connector_instance=0,
                                                                adapter_name=r"\?\USB#VID_34C7&PID_2114#ADAPTER",
                                                                adapter_instance_id=adapter.instance_id, properties={"target_id": 256})])

        result = calibrate_port_map("c1", self.before, after, self.pm)

        self.assertEqual(result["status"], "calibrated")
        self.assertEqual(result["location_paths"], ["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(5)"])
        connectors = self.pm.data["interfaces"][0]["display_connectors"]
        self.assertEqual([c["target_id"] for c in connectors], [8261, 12613])
        self.assertEqual(result["display_connectors"], connectors)

    def test_two_uncalibrated_hdmi_screens_leave_the_hdmi_port_undecided(self):
        from probes.audio_topology.calibration import display_interface_id
        one = PortMap({"interfaces": [{"id": "hdmi", "name": "HDMI", "type": "hdmi"}]})
        gpu = r"PCI\VEN_8086&DEV_7D51\GPU"
        paths = [DisplayPathRecord("a", output_technology=5, adapter_instance_id=gpu), DisplayPathRecord("b", output_technology=5, adapter_instance_id=gpu)]
        self.assertIsNone(display_interface_id(paths[0], one, paths))

    def test_usb_a_ports_never_record_screens(self):
        pm = PortMap({"interfaces": [{"id": "usb-a", "name": "USB-A", "type": "usb-a"}]})
        hub = DeviceNodeRecord(r"USB\VID_1234&PID_5678\A", parent_id=r"USB\ROOT_HUB30\1", location_paths=["PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(6)"])
        with_usb = calibrate_port_map("usb-a", SnapshotRecord(1, "before", MachineRecord()),
                                      SnapshotRecord(1, "after", MachineRecord(), devnodes=[hub], display_paths=[_gpu_path("screen", 4145, 0)]), pm)
        screen_only = calibrate_port_map("usb-a", SnapshotRecord(1, "before", MachineRecord()),
                                         SnapshotRecord(1, "after", MachineRecord(), display_paths=[_gpu_path("screen", 4145, 0)]), pm)
        self.assertEqual(with_usb["status"], "calibrated")
        self.assertEqual(screen_only["status"], "no_candidate")
        self.assertEqual(pm.data["interfaces"][0].get("display_connectors", []), [])

    def test_only_hdmi_on_the_computers_gpu_matches_the_single_hdmi_port(self):
        from probes.audio_topology.calibration import display_interface_id
        one = PortMap({"interfaces": [{"id": "hdmi", "name": "HDMI", "type": "hdmi"}]})
        two = PortMap({"interfaces": [{"id": "hdmi", "name": "HDMI", "type": "hdmi"}, {"id": "hdmi-2", "name": "HDMI", "type": "hdmi"}]})
        gpu = r"PCI\VEN_8086&DEV_7D51\GPU"
        self.assertEqual(display_interface_id(DisplayPathRecord("a", output_technology=5, adapter_instance_id=gpu), one), "hdmi")
        self.assertIsNone(display_interface_id(DisplayPathRecord("b", output_technology=10, adapter_instance_id=gpu), one))
        self.assertIsNone(display_interface_id(DisplayPathRecord("c", output_technology=5, adapter_instance_id=r"USB\VID_34C7&PID_2114\A"), one))
        self.assertIsNone(display_interface_id(DisplayPathRecord("d", output_technology=5, adapter_instance_id=gpu), two))

    def test_several_screens_without_usb_changes_calibrate_usb_c_but_not_hdmi(self):
        after = SnapshotRecord(1, "b", MachineRecord(mtm="21RU"),
                               display_paths=[_gpu_path("first", 8261, 0), _gpu_path("second", 12613, 1)])
        pm = PortMap({"interfaces": [{"id": "c1", "type": "usb-c-tbt"}, {"id": "hdmi", "type": "hdmi"}]})

        self.assertEqual(calibrate_port_map("c1", self.before, after, pm)["status"], "calibrated")
        self.assertEqual(len(pm.data["interfaces"][0]["display_connectors"]), 2)
        self.assertEqual(calibrate_port_map("hdmi", self.before, after, pm)["status"], "ambiguous")
        self.assertNotIn("display_connectors", pm.data["interfaces"][1])


def _gpu_path(path_id: str, target_id: int, connector_instance: int) -> DisplayPathRecord:
    return DisplayPathRecord(path_id, output_technology=10, connector_instance=connector_instance,
                             adapter_name=r"\?\PCI#VEN_8086&DEV_7D51#GPU", adapter_instance_id="PCI\VEN_8086&DEV_7D51\GPU",
                             properties={"target_id": target_id})

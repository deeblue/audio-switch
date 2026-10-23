import tempfile
import unittest
from pathlib import Path
from probes.audio_topology.models import SnapshotRecord, MachineRecord, EndpointRecord, DeviceNodeRecord
from probes.audio_topology.snapshots import save_snapshot, load_snapshot, diff_snapshots

class SnapshotTests(unittest.TestCase):
    def test_round_trip_and_duplicate_ids(self):
        with tempfile.TemporaryDirectory() as tmp:
            snap = SnapshotRecord(1, "now", MachineRecord("LENOVO", "T14p", "21RU"),
                                  [EndpointRecord("same", "render", "active"), EndpointRecord("same", "render", "active")])
            restored = load_snapshot(save_snapshot(snap, "before", Path(tmp)))
            self.assertEqual(len(restored.endpoints), 2)
            self.assertEqual(restored.machine.mtm, "21RU")

    def test_diff_reports_repeated_ids_parent_disappearance_and_location_prefix(self):
        before = SnapshotRecord(1, "a", MachineRecord(), [EndpointRecord("x", "render", "active")],
                                [DeviceNodeRecord("parent", location_paths=["PCIROOT(0)#USB(1)"]), DeviceNodeRecord("child", parent_id="parent")])
        after = SnapshotRecord(1, "b", MachineRecord(), [EndpointRecord("x", "render", "active"), EndpointRecord("x", "render", "active")],
                               [DeviceNodeRecord("new", location_paths=["PCIROOT(0)#USB(1)#DEV(2)"])])
        diff = diff_snapshots(before, after)
        self.assertEqual(len(diff.added_endpoints), 1)
        self.assertEqual([x["instance_id"] for x in diff.removed_devnodes], ["parent", "child"])
        self.assertIn("PCIROOT(0)#USB(1)", diff.common_location_prefixes)

    def test_unchanged_snapshot_has_no_new_location_prefixes(self):
        snap = SnapshotRecord(1, "a", MachineRecord(), devnodes=[DeviceNodeRecord("USB\\x", location_paths=["PCIROOT#USB(1)"])])
        self.assertEqual(diff_snapshots(snap, snap).common_location_prefixes, [])

    def test_diff_reports_location_path_change_for_existing_devnode(self):
        before = SnapshotRecord(1, "a", MachineRecord(), devnodes=[
            DeviceNodeRecord("USB\\speaker", location_paths=["PCIROOT#USB(6)"])
        ])
        after = SnapshotRecord(1, "b", MachineRecord(), devnodes=[
            DeviceNodeRecord("USB\\speaker", location_paths=["PCIROOT#USB(1)"])
        ])

        diff = diff_snapshots(before, after)

        self.assertEqual(diff.added_devnodes, [])
        self.assertEqual(diff.removed_devnodes, [])
        self.assertEqual(len(diff.changed_devnodes), 1)
        changed = diff.changed_devnodes[0]
        self.assertEqual(changed["instance_id"], "USB\\speaker")
        self.assertEqual(changed["before"]["location_paths"], ["PCIROOT#USB(6)"])
        self.assertEqual(changed["after"]["location_paths"], ["PCIROOT#USB(1)"])

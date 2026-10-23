import unittest

from probes.audio_topology.models import EndpointRecord, SnapshotRecord, MachineRecord, to_jsonable


class ModelTests(unittest.TestCase):
    def test_jsonable_serializes_nested_records_and_bytes(self):
        snapshot = SnapshotRecord(1, "2026-10-23T00:00:00Z", MachineRecord(),
                                  [EndpointRecord("id", "render", "active", "USB", raw=b"\x01")])
        value = to_jsonable(snapshot)
        self.assertEqual(value["endpoints"][0]["raw"], "AQ==")
        self.assertEqual(value["schema_version"], 1)

    def test_snapshot_schema_version_is_preserved(self):
        self.assertEqual(to_jsonable(SnapshotRecord(7, "now", MachineRecord()))["schema_version"], 7)

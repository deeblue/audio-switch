import unittest
from types import SimpleNamespace

from probes.audio_topology.native.core_audio import collect_audio_endpoints, _PROPERTY_KEYS


class CoreAudioTests(unittest.TestCase):
    def test_duplicate_names_keep_distinct_endpoint_ids(self):
        devices = [SimpleNamespace(id=f"endpoint-{i}", state=1, properties={_PROPERTY_KEYS["friendly_name"]: "same Realtek name"}) for i in (1, 2)]
        records, errors = collect_audio_endpoints(devices)
        self.assertFalse(errors)
        self.assertEqual([r.endpoint_id for r in records], ["endpoint-1", "endpoint-2"])
        self.assertEqual([r.friendly_name for r in records], ["same Realtek name", "same Realtek name"])

    def test_inactive_endpoint_is_excluded(self):
        records, _ = collect_audio_endpoints([SimpleNamespace(id="inactive", state=2, properties={})])
        self.assertEqual(records, [])

    def test_failed_endpoint_does_not_stop_collection(self):
        bad = SimpleNamespace(id="bad", state=1, properties=object())
        good = SimpleNamespace(id="good", state=1, properties={})
        records, errors = collect_audio_endpoints([bad, good])
        self.assertEqual([r.endpoint_id for r in records], ["good"])
        self.assertEqual(errors[0]["scope"], "bad")

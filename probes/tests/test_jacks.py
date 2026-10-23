import unittest
from probes.audio_topology.native.audio_topology import collect_jacks, collect_jack_sink

class JackTests(unittest.TestCase):
    def test_unsupported_ks_interfaces_are_reported_without_guessing(self):
        jacks, errors = collect_jacks("endpoint-id")
        sink, sink_errors = collect_jack_sink("endpoint-id")
        self.assertEqual(jacks, [])
        self.assertIsNone(sink)
        self.assertTrue(any("尚未實作" in x["error"] for x in errors + sink_errors))

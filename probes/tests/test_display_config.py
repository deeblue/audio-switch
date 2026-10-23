import unittest
from probes.audio_topology.native.display_config import collect_display_paths, match_sink_to_display
from probes.audio_topology.models import DisplayPathRecord

class DisplayConfigTests(unittest.TestCase):
    def test_api_error_is_local_and_recorded(self):
        paths, errors = collect_display_paths(type("BadApi", (), {"query_active_paths": lambda self: (_ for _ in ()).throw(OSError("fail"))})())
        self.assertEqual(paths, [])
        self.assertIn("fail", errors[0]["error"])

    def test_luid_ambiguity_does_not_choose_a_display(self):
        sink = {"adapter_luid": {"high": 0, "low": 1}}
        paths = [DisplayPathRecord("a", adapter_luid={"high": 0, "low": 1}), DisplayPathRecord("b", adapter_luid={"high": 0, "low": 1})]
        result = match_sink_to_display(sink, paths)
        self.assertFalse(result["matched"])
        self.assertEqual(result["candidate_ids"], ["a", "b"])

    def test_edid_unique_match_and_duplicate_edid_ambiguity(self):
        sink = {"manufacturer_id": 123, "product_id": 456}
        one = [DisplayPathRecord("a", edid_manufacturer_id=123, edid_product_code_id=456)]
        self.assertEqual(match_sink_to_display(sink, one)["method"], "edid")
        two = one + [DisplayPathRecord("b", edid_manufacturer_id=123, edid_product_code_id=456)]
        self.assertFalse(match_sink_to_display(sink, two)["matched"])

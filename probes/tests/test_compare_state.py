import unittest

from audio_topology.compare_state import compare_state


class CompareStateTests(unittest.TestCase):
    def test_same_endpoint_defaults_and_names_match(self):
        snapshot = {"endpoints": [{"endpoint_id": "render-1", "friendly_name": "Speaker",
                                   "defaults": {"console": True}}]}
        self.assertEqual({"matches": True, "changes": []}, compare_state(snapshot, snapshot))

    def test_reports_added_removed_default_and_name_changes(self):
        before = {"endpoints": [{"endpoint_id": "a", "state": "Active", "friendly_name": "Old", "defaults": {"console": True}},
                                 {"endpoint_id": "b", "friendly_name": "Gone", "defaults": {}}]}
        after = {"endpoints": [{"endpoint_id": "a", "state": "Active", "friendly_name": "New", "defaults": {"console": False}},
                                {"endpoint_id": "c", "friendly_name": "Added", "defaults": {}}]}
        result = compare_state(before, after)
        self.assertFalse(result["matches"])
        self.assertEqual({"endpoint_presence", "friendly_name", "defaults"}, {change["field"] for change in result["changes"]})

    def test_marks_endpoint_state_transition_as_physical_state_change(self):
        before = {"endpoints": [{"endpoint_id": "a", "state": "Active", "defaults": {}}]}
        after = {"endpoints": [{"endpoint_id": "a", "state": "Unplugged", "defaults": {}}]}
        result = compare_state(before, after)
        self.assertFalse(result["matches"])
        self.assertEqual({"matches": False, "changes": [{
            "endpoint_id": "a", "field": "state", "kind": "physical_state_change",
            "before": "Active", "after": "Unplugged"
        }]}, result)


if __name__ == "__main__":
    unittest.main()

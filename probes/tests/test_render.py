import unittest
from probes.audio_topology.render import render_tree

class RenderTests(unittest.TestCase):
    def test_tree_uses_traditional_chinese_labels_and_source(self):
        output = render_tree({"roots": [{"name":"左側 USB-C", "source":"OEM", "children":[{"name":"speaker", "flow":"render", "category":"A", "short_id":"12345678", "source":"校準", "unresolved":[]}]}]})
        self.assertIn("左側 USB-C [OEM]", output)
        self.assertIn("speaker（render，A 類，12345678）[校準]", output)

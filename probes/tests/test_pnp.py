import unittest
from probes.audio_topology.native.pnp import resolve_endpoint_devnode, walk_ancestors


class FakeApi:
    def __init__(self, parents, fail=None): self.parents, self.fail = parents, fail
    def locate(self, instance):
        if self.fail == "locate": raise OSError("missing")
        return instance
    def device_id(self, node): return node
    def parent(self, node):
        if self.fail == node: raise OSError("parent failure")
        return self.parents.get(node)
    def properties(self, node): return {"friendly_name": node}


class PnpTests(unittest.TestCase):
    def test_ancestor_walk_stops_at_a_device_already_read(self):
        nodes, errors = walk_ancestors("child", FakeApi({"child": "hub", "hub": "root", "root": None}), known={"hub"})
        self.assertEqual([n.instance_id for n in nodes], ["child"])
        self.assertEqual(errors, [])

    def test_endpoint_resolution_keeps_both_strategies_explicit(self):
        self.assertEqual(resolve_endpoint_devnode("endpoint", "PCI\\A"), ("PCI\\A", "device_topology"))
        self.assertEqual(resolve_endpoint_devnode(r"\\?\SWD#MMDEVAPI#node#tail"), (r"SWD\MMDEVAPI\node", "mmdevapi_devnode"))

    def test_walks_parent_chain_to_root(self):
        nodes, errors = walk_ancestors("child", FakeApi({"child": "root", "root": None}))
        self.assertEqual([n.instance_id for n in nodes], ["child", "root"])
        self.assertEqual(nodes[0].parent_id, "root")
        self.assertEqual(errors, [])

    def test_cycle_and_parent_error_are_reported(self):
        nodes, errors = walk_ancestors("a", FakeApi({"a": "b", "b": "a"}))
        self.assertTrue(any("cycle" in e["error"] for e in errors))
        nodes, errors = walk_ancestors("a", FakeApi({}, fail="a"))
        self.assertTrue(errors)

    def test_missing_root_is_a_recorded_error(self):
        nodes, errors = walk_ancestors("a", FakeApi({}, fail="locate"))
        self.assertEqual(nodes, [])
        self.assertEqual(errors[0]["property"], "locate")

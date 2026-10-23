import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from probes.audio_topology import cli
from probes.audio_topology.models import SnapshotRecord, MachineRecord, EndpointRecord

class CliIntegrationTests(unittest.TestCase):
    def test_snapshot_and_diff_commands_use_output_directory(self):
        with tempfile.TemporaryDirectory() as tmp:
            snap = SnapshotRecord(1, "now", MachineRecord(), [EndpointRecord("id", "render", "active")])
            with patch.object(cli, "OUT", Path(tmp)):
                args = cli.build_parser().parse_args(["snapshot", "one"])
                self.assertEqual(cli.run_command(args, lambda: snap), 0)
                args = cli.build_parser().parse_args(["snapshot", "two"])
                self.assertEqual(cli.run_command(args, lambda: snap), 0)
                args = cli.build_parser().parse_args(["diff", "one", "two"])
                self.assertEqual(cli.run_command(args), 0)
                self.assertTrue((Path(tmp) / "one.json").exists())

import unittest

from probes.audio_topology.cli import build_parser


class CliTests(unittest.TestCase):
    def test_calibrate_requires_three_positional_arguments(self):
        parser = build_parser()
        with self.assertRaises(SystemExit):
            parser.parse_args(["calibrate", "usb-c-1", "before"])
        args = parser.parse_args(["calibrate", "usb-c-1", "before", "after"])
        self.assertEqual((args.interface_id, args.before, args.after), ("usb-c-1", "before", "after"))

    def test_required_subcommands_are_available(self):
        parser = build_parser()
        for command in ("tree", "raw", "json"):
            with self.subTest(command=command):
                self.assertEqual(parser.parse_args([command]).command, command)
        for argv in (["snapshot"], ["diff", "before"], ["calibrate", "id", "before"]):
            with self.subTest(argv=argv), self.assertRaises(SystemExit):
                parser.parse_args(argv)

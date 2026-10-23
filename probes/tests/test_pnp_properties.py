import struct
import unittest

from probes.audio_topology.native.pnp import (
    _DEVPROPS,
    decode_capabilities,
    parse_physical_device_location,
)


class PnpPropertyTests(unittest.TestCase):
    def test_devpkey_values_use_system_property_categories(self):
        self.assertEqual(_DEVPROPS["removal_policy"], ("A45C254E-DF1C-4EFD-8020-67D146A850E0", 33))
        self.assertEqual(_DEVPROPS["capabilities"], ("A45C254E-DF1C-4EFD-8020-67D146A850E0", 17))
        self.assertEqual(_DEVPROPS["physical_device_location"], ("540B947E-8B40-45BC-A8A2-6A0B894CBDA2", 9))

    def test_capabilities_reports_removable_flag(self):
        self.assertEqual(decode_capabilities(0x84), {"raw": 0x84, "removable": True})
        self.assertEqual(decode_capabilities(0x80), {"raw": 0x80, "removable": False})

    def test_parses_acpi_pld_location_and_group(self):
        fields = (3 << 3) | (1 << 6) | (2 << 8) | (7 << 10) | (1 << 14) | (0xA5 << 15) | (4 << 23)
        raw = struct.pack("<IIII", 1 | (1 << 7), (1080 << 16) | 1920, fields, 0)

        result = parse_physical_device_location(raw)

        self.assertEqual(result["revision"], 1)
        self.assertTrue(result["ignore_color"])
        self.assertEqual(result["width"], 1920)
        self.assertEqual(result["height"], 1080)
        self.assertEqual(result["panel"], 3)
        self.assertEqual(result["vertical_position"], 1)
        self.assertEqual(result["horizontal_position"], 2)
        self.assertEqual(result["shape"], 7)
        self.assertEqual(result["group_orientation"], 1)
        self.assertEqual(result["group_token"], 0xA5)
        self.assertEqual(result["group_position"], 4)

    def test_short_pld_is_reported_as_unparsed(self):
        result = parse_physical_device_location(b"short")
        self.assertIsNone(result["revision"])
        self.assertIn("error", result)

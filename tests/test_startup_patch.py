# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 shangshang21 and contributors
"""Synthetic fixtures only: these tests do not contain vendor firmware."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('startup_patch', ROOT/'tools/build_startup_patch.py')
patch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(patch)


class StartupPatchTests(unittest.TestCase):
    def setUp(self):
        self.profile = json.loads((ROOT/'tools/compatibility.json').read_text())
        image = bytearray(b'\xff' * self.profile['image_bytes'])
        h = self.profile['hook_offset']
        image[h:h+3] = bytes.fromhex(self.profile['hook_expected'])
        self.image = bytes(image)
        # A test-only synthetic profile, never accepted by the command-line tool.
        self.profile['original_code_sha256'] = patch.code_digest(self.image, self.profile)
        c = self.profile['cave_offset']
        image[h:h+3] = bytes.fromhex(self.profile['hook_replacement'])
        image[c:c+len(patch.STUB)] = patch.STUB
        self.profile['patched_code_sha256'] = patch.code_digest(image, self.profile)

    def test_only_reviewed_offsets_change(self):
        output, differences = patch.build(self.image, self.profile)
        self.assertEqual(len(differences), 72)
        self.assertNotEqual(output, self.image)
        self.assertEqual(len(output), len(self.image))

    def test_all_configuration_bytes_preserved(self):
        a, b = self.profile['excluded_configuration_range']
        varied = bytearray(self.image)
        varied[a:b] = bytes(range(256)) * ((b-a)//256)
        output, _ = patch.build(bytes(varied), self.profile)
        self.assertEqual(output[a:b], varied[a:b])

    def test_wrong_code_rejected(self):
        bad = bytearray(self.image)
        bad[0x100] ^= 1
        with self.assertRaises(patch.CompatibilityError):
            patch.build(bytes(bad), self.profile)

    def test_wrong_size_rejected(self):
        with self.assertRaises(patch.CompatibilityError):
            patch.build(self.image[:-1], self.profile)

    def test_repeat_patch_rejected(self):
        output, _ = patch.build(self.image, self.profile)
        with self.assertRaises(patch.CompatibilityError):
            patch.build(output, self.profile)

    def test_reserved_area_guard_independent_of_digest(self):
        image = bytearray(self.image)
        image[self.profile['cave_offset']+100] = 0
        profile = copy.deepcopy(self.profile)
        profile['original_code_sha256'] = patch.code_digest(image, profile)
        with self.assertRaisesRegex(patch.CompatibilityError, 'not empty'):
            patch.build(image, profile)

    def test_cli_never_overwrites_output(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)
            (path/'owned.bin').write_bytes(self.image)
            (path/'existing.bin').write_bytes(b'keep this file')
            done = subprocess.run([sys.executable, str(ROOT/'tools/build_startup_patch.py'),
                                   '--input', str(path/'owned.bin'), '--output', str(path/'existing.bin')],
                                  capture_output=True)
            self.assertNotEqual(done.returncode, 0)
            self.assertEqual((path/'existing.bin').read_bytes(), b'keep this file')


if __name__ == '__main__':
    unittest.main()

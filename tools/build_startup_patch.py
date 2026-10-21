#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 shangshang21 and contributors
"""Generate the case-specific startup patch locally. Never accesses hardware."""
import argparse
import hashlib
import json
from pathlib import Path

PROFILE_PATH = Path(__file__).with_name('compatibility.json')
# Newly written 8051 stub. The vendor firmware itself is not distributed here.
STUB = bytes.fromhex(
    'c02bc02cc02d12fa8f752b00752caa752d01'
    '90dc397403f0a3e41276127b007d03e4ff12fa8f'
    '7fee7e02120490'
    '752b081276b6a3141276127b447d03e4ff12fa8f'
    'd02dd02cd02b22'
)


class CompatibilityError(ValueError):
    """The input does not match the reviewed code version."""


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def code_digest(image, profile):
    start, end = profile['excluded_configuration_range']
    return sha256(image[:start] + image[end:])


def build(image, profile):
    if len(image) != profile['image_bytes']:
        raise CompatibilityError('Expected exactly one 1 MiB owned-device backup.')
    actual = code_digest(image, profile)
    if actual == profile['patched_code_sha256']:
        raise CompatibilityError('This code version already contains the startup patch.')
    if actual != profile['original_code_sha256']:
        raise CompatibilityError('Unrecognized firmware code. No output was generated.')
    hook = profile['hook_offset']
    cave = profile['cave_offset']
    cave_size = profile['cave_size']
    expected_hook = bytes.fromhex(profile['hook_expected'])
    replacement = bytes.fromhex(profile['hook_replacement'])
    if len(expected_hook) != len(replacement) or len(STUB) != 72:
        raise CompatibilityError('Patch description is inconsistent.')
    if image[hook:hook + len(expected_hook)] != expected_hook:
        raise CompatibilityError('Original hook differs.')
    if image[cave:cave + cave_size] != b'\xff' * cave_size:
        raise CompatibilityError('Reserved code area is not empty.')
    output = bytearray(image)
    output[hook:hook + len(replacement)] = replacement
    output[cave:cave + len(STUB)] = STUB
    permitted = set(range(hook, hook + len(replacement))) | set(range(cave, cave + len(STUB)))
    differences = [i for i, (a, b) in enumerate(zip(image, output)) if a != b]
    if len(differences) != profile['expected_changed_bytes'] or not set(differences) <= permitted:
        raise CompatibilityError('Unexpected patch scope.')
    if code_digest(output, profile) != profile['patched_code_sha256']:
        raise CompatibilityError('Output code differs from the reviewed version.')
    start, end = profile['excluded_configuration_range']
    if image[start:end] != output[start:end]:
        raise CompatibilityError('Device configuration changed.')
    return bytes(output), differences


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', required=True, type=Path, help='Your own original SPI backup')
    parser.add_argument('--output', required=True, type=Path, help='A new file; never overwritten')
    args = parser.parse_args()
    if args.input.resolve() == args.output.resolve():
        parser.error('Input and output must be different files.')
    report_path = args.output.with_name(args.output.name + '.patch-report.json')
    if args.output.exists() or report_path.exists():
        parser.error('Output or report already exists; choose a new destination.')
    profile = json.loads(PROFILE_PATH.read_text(encoding='utf-8'))
    if args.input.stat().st_size != profile['image_bytes']:
        parser.error('Expected a 1 MiB input file.')
    original = args.input.read_bytes()
    try:
        output, differences = build(original, profile)
    except CompatibilityError as error:
        parser.error(str(error))
    with args.output.open('xb') as stream:
        stream.write(output)
    report = {
        'operation': 'local-file-only startup patch',
        'hardware_accessed': False,
        'input_sha256': sha256(original),
        'output_sha256': sha256(output),
        'changed_bytes': len(differences),
        'changed_offsets': [hex(i) for i in differences],
        'configuration_preserved': True,
        'optical_toggle_implemented': False,
        'note': 'Generated firmware retains third-party content; this tool license does not license that content.'
    }
    with report_path.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2)
        stream.write('\n')
    print(json.dumps(report, ensure_ascii=False))


if __name__ == '__main__':
    main()

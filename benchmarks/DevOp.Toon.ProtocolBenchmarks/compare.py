#!/usr/bin/env python3
"""Summarize the full-product benchmark without hiding incorrect round trips."""
import argparse
import json
import statistics

parser = argparse.ArgumentParser()
parser.add_argument('baseline')
parser.add_argument('candidate')
parser.add_argument('--output')
args = parser.parse_args()
with open(args.baseline) as stream:
    baseline = json.load(stream)
with open(args.candidate) as stream:
    candidate = json.load(stream)
if baseline['Rows'] != 2051 or candidate['Rows'] != 2051 or baseline['SourceSha256'] != candidate['SourceSha256']:
    raise SystemExit('Benchmark inputs differ or do not contain all 2051 rows.')

def median(profile, operation, field):
    samples = profile.get(operation)
    return statistics.median(sample[field] for sample in samples) if samples else None

def display(value):
    return 'N/A' if value is None else f'{value:,.2f}'

lines = [
    '# Full product protocol comparison',
    '',
    f"All {candidate['Rows']} source rows; SHA-256 `{candidate['SourceSha256']}`.",
    '',
    'Times and allocations are medians. Compact omission profiles are separate from lossless profiles.',
    '',
    '| Build/profile | Correct | UTF-8 bytes | Gzip | Brotli | Encode ms | Decode ms | Encode allocated bytes | Decode allocated bytes |',
    '|---|---|---:|---:|---:|---:|---:|---:|---:|',
]
notes = []
for report in [baseline, candidate]:
    for profile in report['Profiles']:
        correct = 'error' if profile.get('Error') else str(profile.get('Correct')).lower()
        lines.append(f"| {report['Build']}/{profile['Name']} | {correct} | {profile['Utf8Bytes']:,} | {profile['GzipBytes']:,} | {profile['BrotliBytes']:,} | {display(median(profile, 'Encode', 'Milliseconds'))} | {display(median(profile, 'Decode', 'Milliseconds'))} | {display(median(profile, 'Encode', 'AllocatedBytes'))} | {display(median(profile, 'Decode', 'AllocatedBytes'))} |")
        if profile.get('Error'):
            notes.append(f"- `{report['Build']}/{profile['Name']}` failed; see JSON error evidence.")
        elif profile.get('RoundTripDifference'):
            notes.append(f"- `{report['Build']}/{profile['Name']}` first model difference: `{profile['RoundTripDifference']}`.")

lines.extend([''] + notes + ['', '## Existing-path regression gate', ''])
originals = {profile['Name']: profile for profile in baseline['Profiles']}
passed = True
compared = 0
for profile in candidate['Profiles']:
    original = originals.get(profile['Name'])
    if original is None:
        continue
    compared += 1
    valid = not original.get('Error') and not profile.get('Error')
    valid &= original.get('DecodedRows') == 2051 and profile.get('DecodedRows') == 2051
    valid &= bool(original.get('EncodedSha256')) and original.get('EncodedSha256') == profile.get('EncodedSha256')
    valid &= bool(original.get('DecodedModelSha256')) and original.get('DecodedModelSha256') == profile.get('DecodedModelSha256')
    if original.get('Correct') is not True and valid:
        lines.append(f"- {profile['Name']}: exact baseline output and decoded model preserved; baseline source-roundtrip defect remains documented above.")
    if not valid:
        passed = False
        lines.append(f"- {profile['Name']}: unverified because correctness or execution failed.")
        continue
    for operation in ['Encode', 'Decode']:
        before = median(original, operation, 'Milliseconds')
        after = median(profile, operation, 'Milliseconds')
        original_bytes = median(original, operation, 'AllocatedBytes')
        candidate_bytes = median(profile, operation, 'AllocatedBytes')
        if before is None or after is None or original_bytes is None or candidate_bytes is None:
            passed = False
            lines.append(f"- {profile['Name']} {operation}: missing samples.")
            continue
        ratio = after / before if before else float('inf')
        good = ratio <= 1.05 and candidate_bytes <= original_bytes
        passed &= good
        lines.append(f"- {profile['Name']} {operation}: {'PASS' if good else 'FAIL'}; time {ratio:.3f}x; allocated {candidate_bytes - original_bytes:+,.0f} bytes.")
if compared == 0:
    passed = False
    lines.append('- No comparable existing paths were measured.')
for profile in candidate['Profiles']:
    if profile['Name'] not in originals and profile['ExactPreservationRequired'] and (profile.get('Error') or profile.get('Correct') is not True):
        passed = False
        lines.append(f"- {profile['Name']}: FAIL; new lossless profile does not preserve source data.")
lines.extend(['', f"Gate result: {'PASS' if passed else 'NOT PASSED'}. Repeat runs to establish stable timing beyond noise before release.", '', 'New v4 profiles are measured separately; their performance and correctness must be reviewed before claiming an improvement.', '', 'Absolute process memory includes setup; process peak working set is cumulative. Inspect raw memory samples rather than treating it as an isolated operation peak.', ''])
text = '\n'.join(lines)
if args.output:
    with open(args.output, 'w') as stream:
        stream.write(text)
else:
    print(text)
raise SystemExit(0 if passed else 2)

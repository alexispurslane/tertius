"""Extract diagnostic observations from this partial run, without accepting timed evidence."""
import argparse
from collections import Counter
import gzip
import json
from pathlib import Path
import statistics


def summarize(root):
    phase = json.loads((root / 'phase.json').read_text())
    if phase['phase'] != 'stopped before timed rounds: projected work exceeds remaining cap':
        raise ValueError('This report is only for the recorded capacity stop.')
    result = {'status': 'partial', 'acceptance': 'HOLD', 'matched_rounds_completed': 0,
              'phase': phase, 'cuts': [], 'references': [], 'validation': [],
              'steady_diagnostic_integrity': [], 'diagnostic_passes': {},
              'active_extents': []}
    references = {}
    for tree in ['before', 'after']:
        for mode in ['native', 'fsr']:
            for lane in ['cut', 'steady']:
                path = root / f'diagnostic-{tree}' / mode / lane / 'rows.json.gz'
                data = json.loads(gzip.decompress(path.read_bytes()))
                if not data['Validate'] or not data['Diagnostic'] or data['Smoke']:
                    raise ValueError('Expected validating diagnostics, not timed or smoke rows.')
                if data['ObservationBytesPerPresentation'] != (8 if lane == 'cut' else 0):
                    raise ValueError('Unexpected observation bytes.')
                if data['MeterBins'] != (256 if tree == 'before' else 2304):
                    raise ValueError('Unexpected meter.')
                validations = [row for row in data['Rows'] if row.get('Kind') == 'validation']
                messages = [message for row in validations for message in row['Messages']]
                result['validation'].append({'tree': tree, 'mode': mode, 'lane': lane,
                                             'checks': len(validations), 'messages': messages})
                frames = []
                for row in data['Rows']:
                    if row.get('Kind') == 'cold-reference':
                        key = (mode, row['Label'])
                        references.setdefault(key, {})[tree] = {
                            'stops': row['Last']['Stops'], 'center': row['Last']['Center'],
                            'previous_stops': row['Previous']['Stops'],
                            'previous_center': row['Previous']['Center'], 'captures': row['Frames']}
                    if row.get('Kind') == 'cut':
                        shown = row['Presentations']
                        reference = row['Reference']
                        correct = lambda x: (abs(x['AppliedStops'] - reference['Stops']) <= data['StopTolerance']
                            and all(abs(a - b) <= data['PixelTolerance'] for a, b in zip(x['Center'], reference['Center'])))
                        first_correct = next((i for i, x in enumerate(shown) if correct(x)), None)
                        milliseconds = lambda stamp: (stamp - row['RequestAt']) * 1000 / data['Frequency']
                        if abs(milliseconds(shown[0]['CompletedAt']) - row['FirstPresentationMilliseconds']) > 1e-8:
                            raise ValueError('First fence timestamp differs from reported latency.')
                        if correct(shown[0]) != row['FirstPresentationCorrect']:
                            raise ValueError('First-presentation correctness differs from raw observation.')
                        if first_correct is None:
                            if not row['RightCensored'] or row['CorrectMilliseconds'] is not None:
                                raise ValueError('Missing censoring.')
                        elif row['RightCensored'] or abs(milliseconds(shown[first_correct]['CompletedAt']) - row['CorrectMilliseconds']) > 1e-8:
                            raise ValueError('First correct fence timestamp differs from reported latency.')
                        names = ['Label', 'Tick', 'FirstPresentationMilliseconds', 'FirstEnqueueMilliseconds',
                            'FirstApiReturnMilliseconds', 'FirstApiCorrect', 'FirstPresentationCorrect',
                            'CorrectMilliseconds', 'RightCensored', 'TotalShadingDrawsFirstApi',
                            'TotalShadingDrawsObserved', 'CorrectDraws', 'CorrectPresentations',
                            'FirstApiPresentations', 'ExactFirstPresentationAttribution',
                            'FollowupFramesObserved', 'CorrectFollowupFrames', 'CorrectSnapshotSeconds']
                        result['cuts'].append({'tree': tree, 'mode': mode, **{name: row[name] for name in names},
                            'reference_stops': reference['Stops'], 'reference_center': reference['Center'],
                            'first_applied_stops': shown[0]['AppliedStops'], 'first_center': shown[0]['Center'],
                            'last_observed_completion_ms': milliseconds(shown[-1]['CompletedAt']),
                            'observed_presentations': len(shown),
                            'last_applied_stops': shown[-1]['AppliedStops'], 'last_center': shown[-1]['Center']})
                        frames.append(row['Frames'][0]['Frame'])
                if lane == 'steady':
                    rows = [row for row in data['Rows'] if row.get('Kind') == 'steady' and row['Measured']]
                    frames = [row['Frame'] for row in rows]
                    start = rows[0]['Draws'] - frames[0]['Draws']
                    gpu_ids = [frame['GpuFrame'] for frame in frames]
                    matched_ids = [i for i in gpu_ids if i >= start]
                    result['steady_diagnostic_integrity'].append({'tree': tree, 'mode': mode,
                        'validation_enabled': True, 'instrumented': True, 'ordinary_timed_acceptance': False,
                        'wall_samples': len(frames), 'gpu_ids': gpu_ids, 'matched_gpu_ids': matched_ids,
                        'measured_wall_first_draw_id': start, 'matched_gpu_samples': len(matched_ids),
                        'observation_bytes_per_presentation': data['ObservationBytesPerPresentation'],
                        'capture_requests': sum(frame['CaptureRequested'] for frame in frames),
                        'draws_per_call': sorted(set(frame['Draws'] for frame in frames)),
                        'presentations_per_call': sorted(set(frame['Presentations'] for frame in frames)),
                        'gpu_ids_unique': len(set(gpu_ids)) == len(gpu_ids)})
                timings = {}
                for frame in frames:
                    for timing in frame['Passes']:
                        name = timing['Pass']
                        if name.startswith('linux ') or name in ['exposure', 'exposure adapt']:
                            timings.setdefault(name, []).append(timing['Milliseconds'])
                result['diagnostic_passes'][f'{tree}/{mode}/{lane}'] = {
                    name: {'n': len(values), 'median_ms': statistics.median(values),
                           'minimum_ms': min(values), 'maximum_ms': max(values)}
                    for name, values in timings.items()}
    for (mode, label), trees in references.items():
        a, b = trees['before'], trees['after']
        stops_delta = b['stops'] - a['stops']
        center_delta = [y-x for x, y in zip(a['center'], b['center'])]
        result['references'].append({'mode': mode, 'label': label, **trees,
            'candidate_minus_baseline_stops': stops_delta, 'candidate_minus_baseline_center': center_delta,
            'within_shared_tolerance': abs(stops_delta) <= 1/16 and all(abs(x) <= 2 for x in center_delta)})
    result['shared_reference_agreement'] = all(x['within_shared_tolerance'] for x in result['references'])
    for group in json.loads((root / 'active-extents.json').read_text()):
        counts = Counter(tuple(row[1:]) for row in group['records'])
        result['active_extents'].append({key: group[key] for key in ['tree', 'mode', 'lane']} | {
            'records': len(group['records']), 'extents': [{'render_width': a, 'render_height': b,
            'output_width': c, 'output_height': d, 'count': n} for (a,b,c,d), n in sorted(counts.items())]})
    result['notes'] = [
        'These are one-pass validating diagnostic observations, with extra timestamps and extent logging.',
        'No matched pristine wall/GPU comparison, paired round deltas or performance verdict exists.',
        'Cut latency ends at an offscreen Vulkan fence and includes eight observation bytes per presentation and capture overhead.',
        'No desktop compositor, vblank or scanout measurement exists.',
        'Per-tree correctness uses exposure within 1/16 stop and center RGB within two 8-bit sRGB codes of its own reference.',
        'A failed shared reference blocks shared correctness even when candidate per-tree cuts pass.',
        'Baseline reduction and adaptation share a dispatch. No independent within-dispatch cost exists.',
        'The source comparison includes camera-cut changes and the Design A reflection bundle. It does not isolate a format change.'
    ]
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    args.out.write_text(json.dumps(summarize(args.root), indent=2) + '\n')

"""Validate pristine observations. Local replay supplies correctness evidence only."""
import gzip
import hashlib
import json
import statistics
from pathlib import Path
import tarfile
import tempfile


def analyze_returned(root, require_sizes=True):
    """Reject incomplete canonical returns, verify archived bytes, then analyze raw observations."""
    root = Path(root)
    result = read(root / 'result.json')
    if result.get('skipped'):
        raise ValueError('Canonical collector skipped required output: ' + str(result['skipped']))
    if result.get('status') != 'ok':
        raise ValueError('Canonical job did not succeed: ' + str(result.get('status')))
    folder = root / 'linux-out/return'
    report = read(folder / 'status.json')
    if report['status'] != 'complete' or report['required_evidence_returned'] is not True:
        raise ValueError('Returned evidence is ' + report['status'])
    archive_path = folder / 'evidence.tar.gz'
    if not archive_path.is_file():
        raise ValueError('Returned measurement archive is missing')
    if hashlib.sha256(archive_path.read_bytes()).hexdigest() != report['archive_sha256']:
        raise ValueError('Returned archive hash differs')
    with tempfile.TemporaryDirectory(prefix='issue4-returned-') as temporary:
        extracted = Path(temporary)
        with tarfile.open(archive_path) as archive:
            archive.extractall(extracted, filter='data')
        manifest = read(extracted / 'evidence-manifest.json')
        for entry in manifest['files']:
            path = extracted / entry['path']
            if not path.is_file():
                raise ValueError('Required archived file is missing: ' + entry['path'])
            if path.stat().st_size != entry['bytes'] or hashlib.sha256(path.read_bytes()).hexdigest() != entry['sha256']:
                raise ValueError('Archived file hash differs: ' + entry['path'])
        restoration = read(extracted / 'restoration.json')
        if restoration.get('all_diagnostic_source_restored') is not True:
            raise ValueError('Source restoration was not verified')
        config = read(extracted / 'harness.json')['config']
        for relative, expected in config['retained_diagnostics_sha256'].items():
            path = extracted / 'retained-diagnostics' / relative
            if not path.is_file():
                raise ValueError('Required retained diagnostic is missing: ' + relative)
            if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
                raise ValueError('Retained diagnostic hash differs: ' + relative)
        try:
            return analyze_runs(extracted, [('r1-before', 'before'), ('r1-after', 'after'),
                ('r2-after', 'after'), ('r2-before', 'before')], require_sizes=require_sizes)
        except FileNotFoundError as error:
            raise ValueError('Required measurement output is missing: ' + str(error.filename)) from error


def read(path):
    return json.loads(gzip.decompress(path.read_bytes()) if path.suffix == '.gz' else path.read_bytes())


def analyze_runs(root, visits, require_sizes=True):
    rows, references = [], {}
    for label, tree in visits:
        for mode in ('native', 'fsr'):
            for lane in ('cut', 'steady'):
                folder = Path(root) / label / mode / lane
                path = folder / 'rows.json.gz'
                data = read(path if path.exists() else folder / 'rows.json')
                assert data['Validate'] is False and data['Diagnostic'] is False
                assert data['RecoveryFrames'] == 0
                assert data['ObservationBytesPerPresentation'] == (8 if lane == 'cut' else 0)
                assert all(row['Messages'] == [] for row in data['Rows'] if row['Kind'] == 'validation')
                if lane == 'cut':
                    cuts = [row for row in data['Rows'] if row['Kind'] == 'cut']
                    assert [(row['Label'], row['Tick']) for row in cuts] == [('film-outside', 1417), ('film-return', 1462)]
                    for row in cuts:
                        assert row['TotalShadingDrawsFirstApi'] == row['FirstApiPresentations'] == 1
                        assert row['FollowupFramesObserved'] == 0
                        first = row['Presentations'][0]
                        frequency = data['Frequency']
                        observed_ms = 1000 * (first['CompletedAt'] - row['RequestAt']) / frequency
                        assert abs(observed_ms - row['FirstPresentationMilliseconds']) < 1e-7
                        reference = row['Reference']
                        correct = abs(first['AppliedStops'] - reference['Stops']) <= .0625 and all(
                            abs(x - y) <= 2 for x, y in zip(first['Center'], reference['Center']))
                        assert correct == row['FirstPresentationCorrect']
                        if tree == 'after':
                            assert correct and row['CorrectMilliseconds'] == row['FirstPresentationMilliseconds']
                        if not correct:
                            assert row['RightCensored'] and row['CorrectMilliseconds'] is None
                        references.setdefault((mode, row['Label']), []).append(reference)
                        rows.append(dict(visit=label, tree=tree, mode=mode, lane=lane,
                            case=row['Label'], first_correct=correct, draws=1, presentations=1,
                            followups=0, latency_ms=row['FirstPresentationMilliseconds']))
                else:
                    frames = [row['Frame'] for row in data['Rows'] if row['Kind'] == 'steady' and row['Measured']]
                    assert len(frames) == 96
                    assert all(frame['Draws'] == frame['Presentations'] == 1 and not frame['CaptureRequested'] for frame in frames)
                    assert [frame['GpuFrame'] for frame in frames] == list(range(24, 120))
                    if require_sizes:
                        active = [1280, 720] if mode == 'native' else [848, 477]
                        assert all(frame['ActiveSize'] == active and frame['OutputSize'] == [1280, 720] for frame in frames)
                    wall = [1000 * (frame['EndAt'] - frame['StartAt']) / data['Frequency'] for frame in frames]
                    gpu = [frame['GpuMilliseconds'] for frame in frames[1:]]
                    assert all(value is not None for value in gpu)
                    rows.append(dict(visit=label, tree=tree, mode=mode, lane=lane,
                        wall_samples=96, gpu_samples_within_wall_interval=95,
                        capture_requests=0, observation_bytes=0,
                        wall_ms=dict(median=statistics.median(wall), minimum=min(wall), maximum=max(wall)),
                        gpu_ms=dict(median=statistics.median(gpu), minimum=min(gpu), maximum=max(gpu))))
    for refs in references.values():
        for a in refs:
            for b in refs:
                assert abs(a['Stops'] - b['Stops']) <= .0625
                assert all(abs(x - y) <= 2 for x, y in zip(a['Center'], b['Center']))
    differences = []
    for round_name in ('r1', 'r2'):
        for mode in ('native', 'fsr'):
            pair = {row['tree']: row for row in rows if row['visit'].startswith(round_name + '-')
                    and row['mode'] == mode and row['lane'] == 'steady'}
            if set(pair) == {'before', 'after'}:
                differences.append(dict(round=round_name, mode=mode,
                    wall_median_candidate_minus_baseline_ms=pair['after']['wall_ms']['median'] - pair['before']['wall_ms']['median'],
                    gpu_median_candidate_minus_baseline_ms=pair['after']['gpu_ms']['median'] - pair['before']['gpu_ms']['median']))
    return dict(status='all observation gates passed', visits=visits, rows=rows,
        paired_round_differences=differences, shared_real_references_within_tolerance=True,
        gpu_wall_alignment='GPU identities 25..119 are inside the measured wall interval. Identity 24 is excluded. The last wall draw has no GPU result.')


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--returned', required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps(analyze_returned(args.returned), indent=2))

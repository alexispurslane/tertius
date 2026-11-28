"""Replay saved evidence through the exact proposed globs and canonical collect. Never submit."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import random
import runpy
import shutil
import sys
import tarfile
import tempfile
import time


def forbid_external_effects(event, args):
    if event.startswith('socket.') or event in {'subprocess.Popen', 'os.system', 'os.posix_spawn'}:
        raise RuntimeError('Offline proof forbids network and child processes: ' + event)


sys.addaudithook(forbid_external_effects)
sys.dont_write_bytecode = True
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--out', required=True, type=Path)
args = parser.parse_args()
proposal = Path(__file__).resolve().parent.parent
repo = proposal.parents[4]
args.out.mkdir(parents=True, exist_ok=True)
worker = runpy.run_path(str(proposal / 'worker.py'))
handler = runpy.run_path(str(repo / 'tools/runpod/handler.py'))
analyzer = runpy.run_path(str(proposal / 'analyze_runs.py'))
config = json.loads((proposal / 'config.json').read_text())
assert config['output_globs'] == worker['OUTPUT_GLOBS'] == ['linux-out/return/*']
assert handler['Limits']().output_bytes == worker['OUTPUT_LIMIT'] == 8_000_000
assert not config['approval']['coordinator_reviewed'] and not config['approval']['issue4_explicit_paid_approval']
assert config['approval']['authorized_submissions'] == 0 and not config['submitted']


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


with tempfile.TemporaryDirectory(prefix='issue4-output-proof-') as temporary:
    root = Path(temporary)
    out = root / 'linux-out'
    out.mkdir()
    # Preserve every saved top-level Linux diagnostic, provenance and log file.
    saved = repo / 'docs/research/camera-cut-exposure/design-a-linux-2026-11-28/returned/linux-out'
    saved_files = {}
    for path in sorted(saved.iterdir()):
        if path.is_file():
            shutil.copy2(path, out / path.name)
            saved_files[path.name] = {'bytes': path.stat().st_size, 'sha256': digest(path)}
    shutil.copytree(proposal / 'retained-diagnostics', out / 'retained-diagnostics')
    # Duplicate each saved local visit into both rounds solely to exercise return and analysis mechanics.
    # These are unchanged Mac observations, never fresh Linux timings or independent rounds.
    with tarfile.open(proposal / 'local-harness-evidence.tar.gz') as archive:
        for visit, tree in worker['VISITS']:
            for mode in ('native', 'fsr'):
                for lane in ('cut', 'steady'):
                    path = out / visit / mode / lane / 'rows.json'
                    path.parent.mkdir(parents=True)
                    path.write_bytes(archive.extractfile(f'local-full-scope/{tree}/{mode}/{lane}/rows.json').read())
    for visit, tree in worker['VISITS']:
        with tarfile.open(proposal / 'local-harness-evidence.tar.gz') as archive:
            (out / (visit + '.log')).write_bytes(archive.extractfile(f'local-full-scope/{tree}-probe.log').read())
    # Final-write shapes are simulated and labeled. No new verification or GPU clock reading occurs.
    (out / 'assets-after.json').write_bytes((out / 'assets-before.json').read_bytes())
    (out / 'asset-source-after.json').write_bytes((out / 'asset-source-before.json').read_bytes())
    worker['write'](out / 'harness.json', {'offline_transport_replay': True, 'config': config,
        'worker_sha256': digest(proposal / 'worker.py'), 'fixture_module_sha256': digest(proposal / 'fixture.py')})
    worker['write'](out / 'phase.json', {'phase': 'complete', 'offline_transport_replay': True})
    worker['write'](out / 'restoration.json', {'all_diagnostic_source_restored': True,
        'offline_transport_replay': True, 'note': 'No source was edited or process started in this proof.'})
    (out / 'clocks.csv').write_text('OFFLINE REPLAY ONLY: incompressible reserve for 1920 clock records\n'
        + random.Random(8).randbytes(1920 * 80).hex())
    summary = analyzer['analyze_runs'](out, worker['VISITS'], require_sizes=False)
    worker['write'](out / 'matched-summary.json', summary)
    required = worker['REQUIRED_METADATA']
    assert all((out / name).is_file() for name in required)
    input_bytes = sum(path.stat().st_size for path in out.rglob('*') if path.is_file())
    began = time.monotonic()
    accepted = worker['finish_output'](out, complete=True)
    archive_seconds = time.monotonic() - began
    assert accepted['status'] == 'complete'
    files, skipped = handler['collect'](config['output_globs'], root, handler['Limits']().output_bytes)
    assert not skipped
    assert accepted['returned_base64_bytes'] == sum(map(len, files.values()))
    returned = args.out / 'returned'
    for name, encoded in files.items():
        path = returned / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(base64.b64decode(encoded))
    worker['write'](returned / 'result.json', {'status': 'ok', 'files': sorted(files), 'skipped': skipped,
        'offline_transport_replay': True})
    replay = analyzer['analyze_returned'](returned, require_sizes=False)
    assert len(replay['rows']) == 24
    # Force genuine incompressible overflow. The original evidence and all earlier hashes remain unchanged.
    (out / 'overflow-probe.log').write_bytes(random.Random(4).randbytes(6_100_000))
    rejected = worker['finish_output'](out, complete=True)
    rejected_files, rejected_skipped = handler['collect'](config['output_globs'], root, handler['Limits']().output_bytes)
    assert rejected['status'] == 'overflow' and not rejected['required_evidence_returned']
    assert list(rejected_files) == ['linux-out/return/status.json'] and not rejected_skipped
    rejected_json = json.loads(base64.b64decode(rejected_files['linux-out/return/status.json']))
    assert rejected_json['restoration']['all_diagnostic_source_restored'] is True
    report = {'status': 'PASS: offline output transport only', 'network_and_child_processes': 'forbidden by audit hook',
        'new_gpu_submissions': 0, 'canonical_submit_called': False,
        'representative_inputs': 'All saved top-level Linux output files, eight pinned diagnostic gzip files, '
            'unchanged local full-scope rows repeated into two labeled replay rounds, simulated final writes and clock padding.',
        'analysis_limits': 'Mac rows supply no Linux timing. Repeated replay rounds are not independent measurements. '
            'require_sizes=False only for the archived fixture predating derived size fields. Production analysis defaults to True.',
        'saved_top_level_linux_files': saved_files, 'input_bytes': input_bytes,
        'local_archive_seconds': archive_seconds, 'local_archive_seconds_are_not_linux_performance': True,
        'source_sha256': {name: digest(proposal / name) for name in ['worker.py', 'fixture.py', 'fixture.cs.txt', 'analyze_runs.py', 'config.json', 'emit_proposal.py']},
        'canonical_handler_sha256': digest(repo / 'tools/runpod/handler.py'),
        'canonical_config_sha256': digest(repo / 'tools/runpod/endpoint.json'),
        'pins': {key: config[key] for key in ['baseline_sha', 'candidate_sha', 'baseline_tree', 'candidate_tree', 'local_fixture_csharp_sha256', 'linux_cooked_manifest_sha256']},
        'output_globs': config['output_globs'], 'accepted': accepted,
        'accepted_skipped': skipped, 'accepted_collected_files': {name: {'raw_bytes': len(base64.b64decode(encoded)),
            'base64_bytes': len(encoded)} for name, encoded in files.items()},
        'replayed_analysis_status': replay['status'], 'replayed_analysis_rows': len(replay['rows']),
        'rejected': rejected, 'rejected_skipped': rejected_skipped,
        'rejected_collected_base64_bytes': sum(map(len, rejected_files.values())),
        'regression_tests': '../test_output_budget.py', 'approval': config['approval']}
    worker['write'](args.out / 'offline-check.json', report)
    print(json.dumps({key: report[key] for key in ['status', 'input_bytes', 'accepted_collected_files', 'rejected_collected_base64_bytes', 'replayed_analysis_rows']}, indent=2))

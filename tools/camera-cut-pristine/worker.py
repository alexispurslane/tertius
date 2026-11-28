"""Run only on the approved Linux GPU worker, inside its existing dev shell.

The local preparation leaf never invokes main(). No job is submitted here.
"""
import runpy
import shutil
import argparse
import difflib
import hashlib
import gzip
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tarfile
import time
import urllib.request


OUTPUT_GLOBS = ['linux-out/return/*']
OUTPUT_LIMIT = 8_000_000
FINAL_METADATA_RESERVE = 65_536  # Base64 bytes, included within OUTPUT_LIMIT.
VISITS = [('r1-before', 'before'), ('r1-after', 'after'), ('r2-after', 'after'), ('r2-before', 'before')]
REQUIRED_METADATA = [
    'device.csv', 'initial-memory.csv', 'mirror.json', 'assets.log',
    'assets-before.json', 'assets-after.json', 'asset-source-before.json', 'asset-source-after.json',
    'harness.json', 'before-pristine-build.log', 'after-pristine-build.log',
    'before-pristine-binaries.json', 'after-pristine-binaries.json', 'capacity.json',
    'clocks.csv', 'commands.jsonl', 'matched-summary.json', 'phase.json', 'restoration.json',
    *[label + '.log' for label, _ in VISITS]]


def encoded_bytes(paths):
    """Count the handler's base64 bytes, with padding rounded for each file."""
    return sum(4 * ((path.stat().st_size + 2) // 3) for path in paths)


def finish_output(out, *, complete, error=None):
    """Seal all finished evidence before return. Overflow returns failure metadata only."""
    try:
        return seal_output(out, complete=complete, error=error)
    except Exception as failure:
        returned = out / 'return'
        returned.mkdir(exist_ok=True)
        for path in returned.iterdir():
            path.unlink()
        report = {'status': 'packaging-failed', 'required_evidence_returned': False,
            'error': f'{type(failure).__name__}: {failure}'[:2000],
            'measurement_error': str(error)[:2000] if error else None,
            'note': 'Evidence remains only on this worker. No automatic retry.'}
        for name in ('phase', 'restoration'):
            path = out / (name + '.json')
            if path.is_file():
                report[name] = json.loads(path.read_text())
        write(returned / 'status.json', report)
        return report


def seal_output(out, *, complete, error):
    """Archive evidence once and admit it only with the final metadata reserve intact."""
    returned = out / 'return'
    returned.mkdir(exist_ok=True)
    # A repeated offline check must not leave a previously accepted archive advertised.
    for path in returned.iterdir():
        path.unlink()
    required = [*REQUIRED_METADATA,
        *[f'{label}/{mode}/{lane}/rows.json' for label, _ in VISITS
          for mode in ('native', 'fsr') for lane in ('cut', 'steady')],
        *[f'retained-diagnostics/diagnostic-{tree}/{mode}/{lane}/rows.json.gz'
          for tree in ('before', 'after') for mode in ('native', 'fsr') for lane in ('cut', 'steady')]]
    missing = [name for name in required if not (out / name).is_file()]
    restoration = json.loads((out / 'restoration.json').read_text()) if (out / 'restoration.json').exists() else {}
    phase = json.loads((out / 'phase.json').read_text()) if (out / 'phase.json').exists() else {}
    status = 'failed'
    if complete:
        status = 'complete' if not missing and restoration.get('all_diagnostic_source_restored') is True else 'incomplete'
    archive_path = out / 'evidence.tar.gz'
    paths, duplicates = [], {}
    for path in sorted(out.rglob('*')):
        if not path.is_file() or returned in path.parents or path == archive_path:
            continue
        other = path.with_suffix('') if path.suffix == '.gz' else path.with_suffix('.json.gz')
        # Prefer raw new rows and the pinned gzip diagnostics. Verify equality before omitting a copy.
        redundant = ((path.name == 'rows.json.gz' and 'retained-diagnostics' not in path.parts)
                     or (path.name == 'rows.json' and 'retained-diagnostics' in path.parts))
        if redundant and other.is_file():
            raw, compressed = (other, path) if path.suffix == '.gz' else (path, other)
            if raw.read_bytes() == gzip.decompress(compressed.read_bytes()):
                duplicates[str(path.relative_to(out))] = str(other.relative_to(out))
                continue
        paths.append(path)
    manifest = {'files': [{'path': str(path.relative_to(out)), 'bytes': path.stat().st_size,
                         'sha256': sha(path)} for path in paths], 'verified_redundant_copies': duplicates}
    manifest_path = out / 'evidence-manifest.json'
    # The manifest describes evidence, rather than a previous copy of itself.
    paths = [path for path in paths if path != manifest_path]
    manifest['files'] = [entry for entry in manifest['files'] if entry['path'] != manifest_path.name]
    write(manifest_path, manifest)
    def stable_header(header):
        header.mtime = header.uid = header.gid = 0
        header.uname = header.gname = ''
        header.pax_headers = {}
        return header
    with archive_path.open('wb') as raw_archive:
        with gzip.GzipFile(fileobj=raw_archive, filename='', mode='wb', mtime=0, compresslevel=9) as compressed:
            with tarfile.open(fileobj=compressed, mode='w') as archive:
                for path in [*paths, manifest_path]:
                    archive.add(path, arcname=str(path.relative_to(out)), recursive=False, filter=stable_header)
    archive_encoded = encoded_bytes([archive_path])
    report = {'status': status, 'required_evidence_returned': status == 'complete',
        'error': str(error)[:2000] if error else None, 'missing_required': missing,
        'phase': phase, 'restoration': restoration, 'archive_bytes': archive_path.stat().st_size,
        'archive_base64_bytes': archive_encoded, 'archive_sha256': sha(archive_path),
        'archived_files': len(paths) + 1, 'verified_redundant_copies': len(duplicates),
        'handler_output_limit_bytes': OUTPUT_LIMIT, 'final_metadata_reserve_base64_bytes': FINAL_METADATA_RESERVE,
        'output_globs': OUTPUT_GLOBS, 'returned_base64_bytes': 0}
    if archive_encoded > OUTPUT_LIMIT - FINAL_METADATA_RESERVE:
        report.update(status='overflow', required_evidence_returned=False,
            evidence_worker_path='linux-out/evidence.tar.gz',
            note='No measurement archive returned. All rows and provenance remain only on this worker. No automatic retry.')
    else:
        shutil.move(archive_path, returned / archive_path.name)
    status_path = returned / 'status.json'
    while True:
        write(status_path, report)
        size = encoded_bytes(list(returned.iterdir()))
        if size == report['returned_base64_bytes']:
            break
        report['returned_base64_bytes'] = size
    if encoded_bytes([status_path]) > FINAL_METADATA_RESERVE or size > OUTPUT_LIMIT:
        raise RuntimeError('Final output metadata exceeded its reserved budget.')
    return report


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write(path, value):
    Path(path).write_text(json.dumps(value, indent=2) + '\n')


def checked(command, cwd=None):
    return subprocess.check_output(command, cwd=cwd, text=True).strip()


def source_manifest(tree):
    prefixes = ['flake.nix', 'flake.lock', 'native', 'tools/blue-danube-assets',
                'tools/blender', 'content/tertius.demo/assets/blue-danube']
    paths = checked(['git', 'ls-files', '--', *prefixes], tree).splitlines()
    return {path: sha(tree / path) for path in paths}


def verify_assets(tree, expected_manifest):
    content = (tree / 'content').resolve()
    art = content / 'tertius.demo/bin/assets/blue-danube'
    files = sorted([*art.rglob('model.glb'), art / 'earth/material.zip'])
    if len(files) != 11 or not all(path.is_file() for path in files):
        raise RuntimeError('Need exactly ten cooked GLBs and Earth material.zip.')
    entries = [{'path': str(path.relative_to(art)), 'bytes': path.stat().st_size,
                'sha256': sha(path)} for path in files]
    manifest = ''.join(entry['sha256'] + '  ' + entry['path'] + '\n' for entry in entries)
    actual = hashlib.sha256(manifest.encode()).hexdigest()
    if actual != expected_manifest:
        raise RuntimeError(f'Cooked Linux manifest differs: {actual}')
    source = content / 'tertius.demo/assets/blue-danube'
    cache = content / 'tertius.demo/bin/sources/blue-danube'
    verified = []
    sidecars = sorted(source.rglob('*.import.json'))
    if len(sidecars) != 10:
        raise RuntimeError('Need ten approved model source sidecars.')
    for sidecar in sidecars + [source / 'earth/sources.json']:
        data = json.loads(sidecar.read_text())
        record = data['source'] if 'source' in data else data
        approved = record.get('approved', {}).get('record')
        canonical = {'archives': record.get('archives', {}), 'files': record['files']}
        record_hash = 'sha256:' + hashlib.sha256(json.dumps(
            canonical, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
        if approved != record_hash:
            raise RuntimeError(f'Unapproved source manifest: {sidecar}')
        folder = sidecar.parent.relative_to(source)
        for name, entry in sorted(record['files'].items()):
            path = cache / folder / name
            if path.stat().st_size != entry['bytes'] or 'sha256:' + sha(path) != entry['sha256']:
                raise RuntimeError(f'Cached source differs: {path}')
            verified.append({'path': str(path.relative_to(cache)), 'bytes': entry['bytes'],
                             'sha256': entry['sha256']})
    return {'content_root': str(content), 'manifest_sha256': actual,
            'cooked': entries, 'pinned_sources': verified}


def diagnostic_sources(tree):
    """Add timestamps and extent logging only. Never alter the meter's resource barriers."""
    changes = {}
    renderer = tree / 'src/Tertius.Rendering/VulkanRenderer.cs'
    text = renderer.read_text()
    anchor = '        long number = FrameNumber;'
    if text.count(anchor) != 1:
        raise RuntimeError('CommitDrawnFrame extent anchor changed. Inspect the final source.')
    changes[renderer] = text.replace(anchor, anchor + '\n'
        '        System.Console.Error.WriteLine($"LINUX_ACTIVE {number} '
        '{pending.RenderSize.Width} {pending.RenderSize.Height} '
        '{pending.OutputSize.Width} {pending.OutputSize.Height}");')
    timer = tree / 'src/Tertius.Rendering/Frame/GpuTimer.cs'
    text = timer.read_text()
    anchor = 'public const int MaxPasses = 40;'
    if text.count(anchor) != 1:
        raise RuntimeError('GPU timer query capacity anchor changed.')
    changes[timer] = text.replace(anchor, 'public const int MaxPasses = 64;')
    exposure = tree / 'src/Tertius.Rendering/Passes/ExposurePass.cs'
    text = exposure.read_text()
    clear = '            data: 0\n        );'
    meter = '            groupCountY: MeterGroups(extent.Height),\n            groupCountZ: 1\n        );'
    adapt = '        vk.CmdDispatch(commandBuffer: commandBuffer, groupCountX: 1, groupCountY: 1, groupCountZ: 1);'
    for anchor in [clear, meter, adapt]:
        if text.count(anchor) != 1:
            raise RuntimeError('Exposure dispatch timestamp anchor changed. Inspect the final source.')
    def stamp(label):
        return ('\n        context.Timer.After(commandBuffer: context.CommandBuffer, '
                'slot: context.Frame.Index, pass: new(' + label + ', 0));')
    text = text.replace(clear, clear + stamp('"linux histogram clear"'))
    text = text.replace(meter, meter + stamp('"linux histogram meter"'))
    label = '$"linux reduction+adapt phase{phase}"' if 'uint phase)' in text else '"linux reduction+adapt"'
    text = text.replace(adapt, adapt + stamp(label))
    changes[exposure] = text
    return changes


def remaining_projection(out):
    """Conservative runtime estimate from this same worker, not from Mac frame timings."""
    estimates = []
    for tree in ['before', 'after']:
        for mode in ['native', 'fsr']:
            durations = {}
            real_draws = []
            for lane in ['cut', 'steady']:
                data = json.loads(gzip.decompress((out / 'retained-diagnostics' / ('diagnostic-' + tree) / mode / lane / 'rows.json.gz').read_bytes()))
                frequency = data['Frequency']
                for row in data['Rows']:
                    if row.get('Kind') == 'case-duration':
                        durations[row['Label']] = row['Seconds']
                    elif row.get('Kind') == 'steady':
                        frame = row['Frame']
                        real_draws.append((frame['EndAt'] - frame['StartAt']) / frequency / frame['Draws'])
            if not real_draws or not all(name in durations for name in ['real-outside', 'synthetic-extreme', 'steady']):
                raise RuntimeError('Missing diagnostic case durations for the capacity projection.')
            # A return prefix adds 45 outside frames. Keep the measured maximum draw cost.
            # Synthetic correctness and its recovery loops are outside this pristine timing proposal.
            # Two timed visits per tree/mode remain. Reserve 25% for drift/JIT/GC and 20 s for verification.
            one_visit = (2 * durations['real-outside']
                         + durations['steady'] + 45 * max(real_draws) + 2)
            estimates.append({'tree': tree, 'mode': mode, 'diagnostic_case_seconds': durations,
                              'one_timed_visit_seconds_upper_estimate': one_visit,
                              'max_real_draw_wall_seconds': max(real_draws)})
    return {'per_tree_mode': estimates, 'margin_factor': 1.25,
            'projected_remaining_seconds': 1.25 * 2 * sum(
                row['one_timed_visit_seconds_upper_estimate'] for row in estimates) + 20,
            'model_limit': 'Retained diagnostic validation and timestamps can overestimate pristine work. '
                           'This is a capacity estimate, not a completion guarantee.'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', required=True, type=Path)
    args = parser.parse_args()
    config = json.loads(args.config.read_text())
    approval = config['approval']
    if not approval['issue4_explicit_paid_approval'] or not approval['coordinator_reviewed']:
        raise RuntimeError('INERT: new Issue #4 paid approval and coordinator review are required before worker execution.')
    if config['baseline_sha'] != config['main_baseline_sha']:
        raise RuntimeError('Baseline must be the reviewed main commit with allocator fix #35.')
    if config['output_globs'] != OUTPUT_GLOBS:
        raise RuntimeError('Output globs must select only the sealed return folder.')
    for key in ['baseline_sha', 'candidate_sha', 'issue3_final_sha', 'main_baseline_sha']:
        value = config[key]
        if not isinstance(value, str) or len(value) != 40 or any(c not in '0123456789abcdef' for c in value):
            raise RuntimeError(f'Pin final full commit SHA: {key}')
    base = Path.cwd()
    out = base / 'linux-out'
    out.mkdir(exist_ok=False)
    deadline = time.monotonic() + config['command_timeout_seconds'] - 10
    env = os.environ.copy()
    env['PYTHONDONTWRITEBYTECODE'] = '1'
    started = time.monotonic()
    original = {}
    provenance = {}
    complete = False
    failure = None
    def phase(name):
        write(out / 'phase.json', {'phase': name, 'elapsed_seconds': time.monotonic() - started})
    def run(command, log, tree=None, extra=None, cap=140):
        remaining = min(cap, deadline - time.monotonic())
        if remaining < 1:
            raise TimeoutError('Approved command time cap reached.')
        child_env = env.copy()
        child_env.update(extra or {})
        began = time.monotonic()
        with (out / log).open('w') as stream:
            child = subprocess.Popen(command, cwd=tree, env=child_env, stdout=stream,
                                     stderr=subprocess.STDOUT, start_new_session=True)
            timed_out = False
            try:
                code = child.wait(timeout=remaining)
            except subprocess.TimeoutExpired:
                timed_out = True
                os.killpg(child.pid, signal.SIGTERM)
                try:
                    child.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    os.killpg(child.pid, signal.SIGKILL)
                    child.wait()
                code = child.returncode
        with (out / 'commands.jsonl').open('a') as stream:
            stream.write(json.dumps({'command': command, 'cwd': str(tree), 'exit': code,
                                     'seconds': time.monotonic() - began, 'timed_out': timed_out}) + '\n')
        if timed_out:
            raise TimeoutError(f'Command reached its time limit: {log}')
        if code:
            raise RuntimeError(f'Command failed with {code}: {log}')
    def build(tree, label):
        run(['dotnet', 'build', 'tests/Tertius.Host.Tests', '-c', 'Release', '-nologo', '-v:q',
             '-p:RunAnalyzers=false'], label + '-build.log', tree)
    def measure(tree, label, diagnostic=False):
        folder = out / label
        run(['dotnet', 'tests/Tertius.Host.Tests/bin/Release/net11.0/Tertius.Host.Tests.dll',
             '-class', 'Tertius.Host.Tests.LinuxExposureMeasurements', '-explicit', 'only',
             '-maxThreads', '1', '-noColor'], label + '.log', tree,
            {'TERTIUS_LINUX_OUT': str(folder), 'TERTIUS_LINUX_BATCH': '1',
             'TERTIUS_LINUX_DIAGNOSTIC': str(int(diagnostic)),
             'TERTIUS_LINUX_VALIDATE': str(int(diagnostic)),
             'TERTIUS_LINUX_SCRIPTS': str(base / 'linux-scripts')}, cap=config['pristine_process_timeout_seconds'])
    try:
        phase('verify device and pinned trees')
        run(['nvidia-smi', '--query-gpu=name,uuid,driver_version,pci.bus_id', '--format=csv'], 'device.csv')
        device_lines = (out / 'device.csv').read_text().splitlines()
        if len(device_lines) != 2 or config['gpu_name'] not in device_lines[1] or config['driver'] not in device_lines[1]:
            raise RuntimeError('GPU model or driver differs from the approved matched configuration.')
        trees = {'before': base / 'before', 'after': base / 'after'}
        for name, tree in trees.items():
            expected = config['baseline_sha' if name == 'before' else 'candidate_sha']
            if checked(['git', 'rev-parse', 'HEAD'], tree) != expected:
                raise RuntimeError('Tree commit differs from reviewed input.')
            if name == 'before' and checked(['git', 'rev-parse', 'HEAD^{tree}'], tree) != config['baseline_tree']:
                raise RuntimeError('Baseline tree hash differs from final C1 source.')
            allocator = checked(['git', 'rev-parse', 'HEAD:src/Tertius.Rendering/Vulkan/DeviceAllocator.cs'], tree)
            if allocator != config['allocator_blob']:
                raise RuntimeError('Both trees must contain the reviewed allocator bytes.')
            if checked(['git', 'diff', '--name-only'], tree):
                raise RuntimeError('Worker tree is dirty before instrumentation.')
            provenance[name] = source_manifest(tree)
        if provenance['before'] != provenance['after']:
            raise RuntimeError('Asset sources, converter or dev shell differ between trees.')
        write(out / 'asset-source-before.json', provenance)
        run(['nvidia-smi', '--query-gpu=memory.total,memory.free', '--format=csv,noheader,nounits'], 'initial-memory.csv')
        initial_memory = [int(value.strip()) for value in (out / 'initial-memory.csv').read_text().strip().split(',')]
        if initial_memory[1] < config['minimum_free_gpu_mib']:
            raise RuntimeError('GPU has insufficient free memory for the reviewed comparison.')
        phase('approved NOAA mirror then one canonical asset setup')
        entry = json.loads((trees['before'] / 'content/tertius.demo/assets/blue-danube/earth/sources.json').read_text())['files']['elevation.dods']
        if entry['bytes'] != 9370338 or entry['sha256'] != 'sha256:a14135cf20fe2d0a7a18af72a0999483735c1f7e47cf3dbdc24d819beda61b04':
            raise RuntimeError('NOAA pin changed.')
        with urllib.request.urlopen(entry['stagingMirror'], timeout=35) as reply:
            data = reply.read(entry['bytes'] + 1)
        if len(data) != entry['bytes'] or 'sha256:' + hashlib.sha256(data).hexdigest() != entry['sha256']:
            raise RuntimeError('NOAA mirror bytes differ.')
        staging = base / 'linux-source-staging'
        staging.mkdir()
        (staging / 'elevation.dods').write_bytes(data)
        write(out / 'mirror.json', {'url': entry['stagingMirror'], 'bytes': len(data), 'sha256': entry['sha256']})
        run(['bash', 'tools/blue-danube-assets/setup.sh', '--staging', str(staging)],
            'assets.log', trees['before'], cap=220)
        assets = verify_assets(trees['before'], config['linux_cooked_manifest_sha256'])
        write(out / 'assets-before.json', assets)
        env['TERTIUS_CONTENT_ROOT'] = assets['content_root']
        # The fixture is identical in both trees. Production code stays byte-identical for timed measurements.
        fixture = base / 'fixture.py'
        namespace = {}
        exec(compile(fixture.read_text(), str(fixture), 'exec'), namespace)
        source = namespace['SOURCE']
        fixture_hash = hashlib.sha256(source.encode()).hexdigest()
        if fixture_hash != config['local_fixture_csharp_sha256']:
            raise RuntimeError('Fixture bytes differ from the pinned proposal.')
        write(out / 'harness.json', {'config': config, 'fixture_csharp_sha256': fixture_hash,
              'worker_sha256': sha(__file__), 'fixture_module_sha256': sha(fixture)})
        for tree in trees.values():
            fixture_path = tree / 'tests/Tertius.Host.Tests/LinuxExposureMeasurements.cs'
            if fixture_path.exists():
                raise RuntimeError('Measurement source already exists.')
            fixture_path.write_text(source)
        # Reuse immutable validating diagnostics. This worker never instruments production source.
        phase('verify retained diagnostics and build both pristine trees once')
        retained = base / 'retained-diagnostics'
        for relative, expected in config['retained_diagnostics_sha256'].items():
            path = retained / relative
            if sha(path) != expected:
                raise RuntimeError('Retained diagnostic bytes changed.')
        shutil.copytree(retained, out / 'retained-diagnostics')
        for name, tree in trees.items():
            build(tree, name + '-pristine')
            write(out / (name + '-pristine-binaries.json'), {
                'commit': checked(['git', 'rev-parse', 'HEAD'], tree),
                'fixture_csharp_sha256': fixture_hash,
                'production_source_clean': not checked(['git', 'diff', '--name-only'], tree),
                'rendering_assembly_sha256': sha(tree / 'src/Tertius.Rendering/bin/Release/net11.0/Tertius.Rendering.dll'),
                'host_test_assembly_sha256': sha(tree / 'tests/Tertius.Host.Tests/bin/Release/net11.0/Tertius.Host.Tests.dll')})
        capacity = remaining_projection(out)
        capacity['remaining_command_seconds'] = deadline - time.monotonic()
        capacity['fits_remaining_cap'] = capacity['projected_remaining_seconds'] <= capacity['remaining_command_seconds']
        write(out / 'capacity.json', capacity)
        if not capacity['fits_remaining_cap']:
            phase('stopped before timed rounds: projected work exceeds remaining cap')
            raise RuntimeError('Capacity projection exceeds the approved command cap. No automatic resubmission.')
        # Log clocks while all matched runs execute. No production instrumentation remains.
        phase('reversed matched rounds; each ordinary lane discards its first 24 frames')
        clocks = (out / 'clocks.csv').open('w')
        monitor = subprocess.Popen(['nvidia-smi', '--query-gpu=timestamp,uuid,clocks.sm,clocks.mem,temperature.gpu,power.draw',
                                    '--format=csv', '-l', '1'], stdout=clocks, stderr=subprocess.STDOUT)
        try:
            for round_number, order in enumerate([['before', 'after'], ['after', 'before']], 1):
                for name in order:
                    measure(trees[name], f'r{round_number}-{name}')
        finally:
            monitor.terminate()
            monitor.wait(timeout=5)
            clocks.close()
        analyzer = runpy.run_path(str(base / 'analyze_runs.py'))['analyze_runs']
        write(out / 'matched-summary.json', analyzer(out, VISITS))
        phase('verify assets and source restoration')
        after_assets = verify_assets(trees['before'], config['linux_cooked_manifest_sha256'])
        if after_assets != assets:
            raise RuntimeError('Shared assets changed during measurement.')
        for name, tree in trees.items():
            if checked(['git', 'diff', '--name-only'], tree) or source_manifest(tree) != provenance[name]:
                raise RuntimeError('Source changed during measurement.')
        write(out / 'assets-after.json', after_assets)
        write(out / 'asset-source-after.json', {name: source_manifest(tree) for name, tree in trees.items()})
        complete = True
    except BaseException as error:
        failure = f'{type(error).__name__}: {error}'
        raise
    finally:
        try:
            for path, data in original.items():
                path.write_bytes(data)
            restored = all(not checked(['git', 'diff', '--name-only'], base / name) for name in ['before', 'after'])
            write(out / 'restoration.json', {'all_diagnostic_source_restored': restored,
                'elapsed_seconds': time.monotonic() - started,
                'note': 'A failed phase is not valid timed evidence. No automatic resubmission.'})
            if not restored:
                complete = False
                failure = 'Source restoration verification failed.'
        except Exception as error:
            complete = False
            failure = f'Restoration verification failed: {error}'
            write(out / 'restoration.json', {'all_diagnostic_source_restored': False, 'error': failure[:2000]})
        phase('complete' if complete else 'failed')
        report = finish_output(out, complete=complete, error=failure)
        if report['status'] != 'complete':
            raise RuntimeError('Returned evidence is ' + report['status'] + '. No automatic resubmission.')


if __name__ == '__main__':
    main()

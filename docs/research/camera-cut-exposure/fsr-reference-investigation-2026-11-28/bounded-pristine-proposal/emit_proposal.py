"""Emit an inert review copy of the existing canonical CLI with a named harness tree."""
import argparse
import hashlib
import json
import runpy
import os
from pathlib import Path
import shlex


# Conservative Linux launch bounds, not the host's POSIX shell text limit. Allow 64 KiB for environment
# strings and 128 KiB for all argument/environment strings and pointers. Each command gets at most 32 KiB.
SHELL = ['nix', 'develop', '/nix/var/nix/profiles/tertius-dev', '--offline', '--command', 'bash', '-c']


def command_bounds(commands, environment):
    """Count UTF-8 strings with NUL terminators and 64-bit argv/envp pointers before emitting."""
    environment_bytes = sum(len((key + '=' + value).encode()) + 1 for key, value in environment.items())
    if environment_bytes > 65536:
        raise ValueError('The environment exceeds the reserved 65536-byte budget')
    sizes, aggregate = [], []
    for command in commands:
        size = len(command.encode()) + 1
        if size > 32768:
            raise ValueError('A command argument exceeds the conservative 32768-byte budget')
        args = [*SHELL, command]
        pointers = 8 * (len(args) + len(environment) + 2)
        # Reserve room for up to 4096 environment entries, even when the local environment is smaller.
        pointer_reserve = 8 * (len(args) + 4096 + 2)
        total = sum(len(arg.encode()) + 1 for arg in args) + 65536 + max(pointers, pointer_reserve)
        if total > 131072:
            raise ValueError('Aggregate argument/environment budget exceeds 131072 bytes')
        sizes.append(size)
        aggregate.append(total)
    return {'command_bytes_including_nul': sizes, 'argument_limit_bytes': 32768,
        'environment_bytes_observed': environment_bytes, 'environment_reserve_bytes': 65536,
        'aggregate_bytes_with_reserve': aggregate, 'aggregate_limit_bytes': 131072,
        'environment_entry_reserve': 4096, 'pointer_bytes': 8,
        'runtime_environment_verified': False}


def emit(root, out):
    """Name immutable runtime inputs, preserve the measured pins, and stop PowerShell before submission."""
    config = json.loads((root / 'config.json').read_text())
    if (config['approval']['coordinator_reviewed'] or config['approval']['issue4_explicit_paid_approval']
            or config['approval']['authorized_submissions'] != 0 or config['submitted']):
        raise ValueError('This emitter accepts only the inert proposal.')
    transport = json.loads((root / 'transport.json').read_text())
    pin = transport['harness_sha']
    if len(pin) != 40 or any(c not in '0123456789abcdef' for c in pin):
        raise ValueError('Name the full immutable harness commit')
    payload = transport['payload_sha256']
    for name, digest in payload.items():
        if hashlib.sha256((root / name).read_bytes()).hexdigest() != digest:
            raise ValueError('Proposal input differs from the immutable harness: ' + name)
    launch_path = Path(__file__).resolve().parents[5] / 'tools/camera-cut-pristine/external_launch.py'
    program = launch_path.read_text()
    record = runpy.run_path(str(launch_path))['approval_record'](config)
    shell = 'python3 -I -c ' + shlex.quote(program) + ' --authorization-json ' + shlex.quote(json.dumps(record, sort_keys=True))
    proposed_record = {**record, 'coordinator_reviewed': True, 'issue4_explicit_paid_approval': True,
        'duration_policy_approved': True, 'authorized_submissions': 1}
    proposed_shell = 'python3 -I -c ' + shlex.quote(program) + ' --authorization-json ' + shlex.quote(json.dumps(proposed_record, sort_keys=True))
    bounds = command_bounds([shell, proposed_shell], {key: value for key, value in os.environ.items() if not key.startswith('RUNPOD_')})
    argv = ['python3', '-I', 'tools/runpod/gpu_job.py', 'submit', '--agent', 'issue4-exposure',
        '--tree', 'before=' + config['baseline_sha'], '--tree', 'after=' + config['candidate_sha'],
        '--tree', 'harness=' + pin, '--run', shell, '--timeout', str(config['command_timeout_seconds']),
        '--out', config['result_folder'] or '/tmp/tertius-issue37-one-attempt/returned']
    for pattern in config['output_globs']:
        argv += ['--output', pattern]
    # The body shape and hidden-ref names match canonical submit. This is never sent.
    trees = {name: {'sha': sha, 'ref': 'refs/gpu-jobs/2026-11-28/issue4-exposure/' + sha}
        for name, sha in [('before', config['baseline_sha']), ('after', config['candidate_sha']), ('harness', pin)]}
    body = {'input': {'protocol': 3, 'repository': 'https://github.com/alexispurslane/tertius.git',
        'trees': trees, 'commands': [shell], 'outputs': config['output_globs'], 'seconds': 1920},
        'policy': {'executionTimeout': 1980000, 'ttl': 7200000}}
    proposed_body = {**body, 'input': {**body['input'], 'commands': [proposed_shell]}}
    body_bytes = len(json.dumps(body).encode())
    proposed_body_bytes = len(json.dumps(proposed_body).encode())
    if max(body_bytes, proposed_body_bytes) > 65536:
        raise ValueError('The proposed service request exceeds the conservative 65536-byte budget')
    command = ("throw 'INERT: duration policy and paid submission remain unapproved. This is a review copy only.'\n"
        + '& ' + ' '.join("'" + word.replace("'", "''") + "'" for word in argv) + '\n')
    out.write_text(command)
    metadata = {'submitted': False, 'proposal_only': True, 'approval': config['approval'],
        'output_globs': config['output_globs'], 'command_timeout_seconds': config['command_timeout_seconds'],
        'conditional_execution_seconds': config['canonical_execution_timeout_seconds'],
        'conditional_canonical_reservation_usd': config['worst_case_usd'],
        'launchable_under_checked_in_policy': False,
        'harness_sha': pin, 'prepare_sha256': transport['prepare_sha256'],
        'manifest_sha256': transport['manifest_sha256'], 'payload_sha256': payload,
        'command_bounds': bounds, 'request_body_bytes': body_bytes,
        'request_body_budget_bytes': 65536, 'conditional_request_body': body,
        'future_authorization': 'External record is embedded in the command. All four approval fields must change together after parent approval.',
        'external_approval_record': record,
        'representative_approved_record': proposed_record,
        'representative_approved_request_body': proposed_body,
        'representative_approved_request_body_bytes': proposed_body_bytes,
        'representative_approved_command_sha256': hashlib.sha256(proposed_shell.encode()).hexdigest(),
        'inert_command_sha256': hashlib.sha256(shell.encode()).hexdigest(),
        'canonical_argv_inert': argv,
        'canonical_argv_after_parent_approval': [proposed_shell if arg == shell else arg for arg in argv],
        'external_program_sha256': hashlib.sha256(program.encode()).hexdigest(),
        'submit_command_sha256': hashlib.sha256(command.encode()).hexdigest()}
    out.with_suffix('.json').write_text(json.dumps(metadata, indent=2) + '\n')
    return metadata


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    emit(Path(__file__).resolve().parent, args.out)

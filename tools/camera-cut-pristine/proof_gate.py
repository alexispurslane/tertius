"""Offline test shell. Stop the real worker at its first measurement-directory creation."""
import json
from pathlib import Path
import runpy
import shlex
import subprocess
import sys


def worker_gate(event, args):
    if event.startswith('socket.') or event in {'subprocess.Popen', 'os.system', 'os.posix_spawn'}:
        raise RuntimeError('Offline worker gate forbids external effects: ' + event)
    if event == 'os.mkdir' and Path(args[0]).name == 'linux-out':
        Path('gate.json').write_text(Path(sys.argv[-1]).read_text())
        print('REAL_WORKER_REACHED_HARMLESS_GATE', flush=True)
        raise SystemExit(0)


if sys.argv[1] == '--worker':
    sys.argv = ['worker.py', '--config', sys.argv[2]]
    sys.addaudithook(worker_gate)
    runpy.run_path('worker.py', run_name='__main__')
else:
    # The real handler launches this shell with the final command as one argument.
    # Only the last child launch is redirected to the gated, unchanged worker.
    argv = shlex.split(sys.argv[1])
    assert argv[:3] == ['python3', '-I', '-c']
    program = argv[3]
    sys.argv = ['-c', *argv[4:]]
    real_run = subprocess.run

    def gated_run(command, **kwargs):
        assert command[1:] == ['-I', 'worker.py', '--config', 'config.runtime.json']
        return real_run([sys.executable, '-I', __file__, '--worker', command[-1]], **kwargs)

    subprocess.run = gated_run
    exec(compile(program, '<external launch>', 'exec'), {'__name__': '__main__'})

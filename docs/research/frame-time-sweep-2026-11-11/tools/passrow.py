"""Prints one row per run for one pass of the engine's pass timers: its mean and median, the frame span and the GPU clock.

usage: passrow.py <pass name> <trace folder>...

Each folder holds run/gpu-timings.json. A pass that draws other work in each frame, such as the environment faces,
has a mean that differs from its median, so the row gives both. On MoltenVK a pass's own time can hold part of the
next pass's work (tools/README.md, "GPU trace").
"""
import json, os, sys
name, folders = sys.argv[1], sys.argv[2:]
print(f'| Run | {name}, mean ms | {name}, median ms | Frame span, median ms | GPU clock, MHz |')
print('|---|---:|---:|---:|---:|')
for folder in folders:
    path = os.path.join(folder, 'run', 'gpu-timings.json')
    if not os.path.exists(path):
        print(f'| `{os.path.basename(folder.rstrip("/"))}` | no record | | | |')
        continue
    run = json.load(open(path))
    row = next((p for p in run['passes'] if p['pass'] == name), None)
    clock = run.get('gpuClock', {}).get('meanMhz')
    cells = ['', ''] if row is None else [f"{row['meanMs']:.3f}", f"{row['medianMs']:.3f}"]
    print(f'| `{os.path.basename(folder.rstrip("/"))}` | {cells[0]} | {cells[1]} | {run["frame"]["medianMs"]:.3f} | '
          + ('none' if clock is None else f'{clock:.1f}') + ' |')

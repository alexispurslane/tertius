"""Check the image-measure tests against isolated, scripted mistakes."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile

HERE = Path(__file__).resolve().parent
MEASURE_MUTATIONS = [
    ('ignore-spatial-grain', "spatial_rms=float(np.sqrt(np.mean(high ** 2)))", 'spatial_rms=0.0'),
    ('ignore-temporal-grain', "temporal_rms=float(np.sqrt(np.mean(np.diff(high, axis=0) ** 2)))", 'temporal_rms=0.0'),
    ('count-brightness-as-grain', "high = frames - gaussian_filter(frames, sigma=(0, 1.5, 1.5), mode='reflect')", 'high = frames'),
    ('ignore-brightness-loss', "mean_change_percent=float(100 * (variant.mean() / baseline.mean() - 1))", 'mean_change_percent=0.0'),
    ('ignore-paired-error', "paired_rms=float(np.sqrt(np.mean((variant - baseline) ** 2)))", 'paired_rms=0.0'),
    ('ignore-color-change', 'changed = variant.mean(axis=0)', 'changed = baseline.mean(axis=0)'),
    ('skip-local-patches', 'for row in range(2):', 'for row in range(0):'),
]


SPREAD_MUTATIONS = [
    ('redistribute-the-centers-retained-share', '/ 25', '/ 24'),
    ('cross-surface-boundaries', '(surface[p] == surface[q])[..., None]', 'np.ones_like(surface[p], dtype=bool)[..., None]'),
    ('stop-spreading', 'result[p] += agrees * (image[q] - image[p]) / 25', 'result[p] += 0'),
    ('break-reciprocity-with-receiver-mask', 'mask[p if side > 0 else q]', 'mask[p]'),
    ('disconnect-nearby-coordinate-phases', 'exchange(dx,dy,1/16)', 'exchange(6*dx,6*dy,1/16)'),
    ('make-exchange-unstable', 'exchange(dx,dy,1/16)', 'exchange(dx,dy,1/4)'),
    ('accept-either-tangent-plane', 'np.maximum(np.sum(first_tangent**2, axis=-1), np.sum(second_tangent**2, axis=-1))', 'np.minimum(np.sum(first_tangent**2, axis=-1), np.sum(second_tangent**2, axis=-1))'),
    ('use-the-larger-endpoint-footprint', 'np.minimum(first_radius, second_radius)', 'np.maximum(first_radius, second_radius)'),
    ('include-normal-separation-in-tangent-distance', 'first_tangent = apart - np.sum(apart * first_normal, axis=-1)[...,None] * first_normal', 'first_tangent = apart'),
    ('exchange-outside-the-surface-footprint', 'distance_squared <= radius**2', 'distance_squared > radius**2'),
    ('skip-the-surface-footprint', 'if footprint is not None:', 'if False:'),
]


def main():
    results = []
    with tempfile.TemporaryDirectory(prefix='issue-5-measures-') as scratch:
        folder = Path(scratch)
        for filename in ['measure.py', 'spread_model.py', 'test_measure.py', 'test_spread_model.py']:
            (folder / filename).write_text((HERE / filename).read_text())
        for filename, mutations, tests in [('measure.py', MEASURE_MUTATIONS, 'test_measure.py'),
                                            ('spread_model.py', SPREAD_MUTATIONS, 'test_spread_model.py')]:
            source = (HERE / filename).read_text()
            for name, before, after in mutations:
                assert source.count(before) == 1, name
                (folder / filename).write_text(source.replace(before, after))
                run = subprocess.run([sys.executable, '-B', str(folder / tests)],
                    capture_output=True, text=True, timeout=60)
                result = 'killed' if run.returncode and 'FAIL:' in run.stderr else 'survived-or-error'
                results.append(dict(name=name, result=result))
            (folder / filename).write_text(source)
    print(json.dumps(results, indent=2))
    return int(any(row['result'] != 'killed' for row in results))


if __name__ == '__main__':
    raise SystemExit(main())

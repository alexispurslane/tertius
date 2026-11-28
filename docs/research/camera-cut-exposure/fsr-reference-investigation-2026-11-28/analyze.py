"""Read raw scene pixels independently of either engine's ExposureMeter implementation."""
import array
import hashlib
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent
WEIGHTS = (0.2126, 0.7152, 0.0722)


def floats(path):
    values = array.array('f')
    values.frombytes(path.read_bytes())
    return values


def histogram(luminances, lowest, count, exposure=0):
    bins = [0] * count
    for value in luminances:
        stops = math.log2(max(value * 2**exposure, 2**lowest))
        bins[max(0, min(count - 1, math.floor((stops - lowest) * 8)))] += 1
    return bins


def reading(bins, lowest, producing_exposure=0):
    total = sum(bins)
    low, high = total * .1, total * .9
    end = weighted = kept = 0
    edge = None
    for index, count in enumerate(bins):
        start = end
        end += count
        inside = max(0, min(end, high) - max(start, low))
        weighted += inside * (lowest + (index + .5) / 8)
        kept += inside
        if start < high <= end:
            edge = lowest + (index + (high - start) / count) / 8
    mean = weighted / kept - producing_exposure
    edge -= producing_exposure
    target = min(math.log2(.18) - mean, -edge) - 13
    return dict(total=total, trimmed_scene_mean_stops=mean,
                scene_bright_edge_stops=edge, unclamped_target_stops=target,
                clamped_target_stops=max(-14, min(10, target)))


def exact_target(luminances):
    stops = sorted(math.log2(value) for value in luminances)
    low, high = len(stops) * .1, len(stops) * .9
    kept = weighted = 0
    for index, value in enumerate(stops):
        inside = max(0, min(index + 1, high) - max(index, low))
        weighted += value * inside
        kept += inside
    mean = weighted / kept
    edge = stops[math.ceil(high) - 1]
    return max(-14, min(10, min(math.log2(.18) - mean, -edge) - 13))


def main():
    rows, sequences, identities, source_controls = [], [], [], []
    for tree in ('before', 'after'):
        for mode in ('fsr', 'native'):
            folder = ROOT / tree / (mode + '-auto')
            sequence = json.loads((folder / 'sequence.json').read_text())
            assert sequence['Validation'] == []
            for phase in ('still', 'continuous'):
                samples = [row for row in sequence['Sequence'] if row['Phase'] == phase]
                tail = [row['Frame']['Stops'] for row in samples[-24:]]
                sequences.append(dict(tree=tree, mode=mode, phase=phase,
                    final_stops=samples[-1]['Frame']['Stops'], last24_min_stops=min(tail),
                    last24_max_stops=max(tail), count=len(samples),
                    draws=sum(row['Frame']['Draws'] for row in samples),
                    jitter_xy=[samples[-1]['RenderParams'][axis] for axis in ('Y', 'Z')]))
            for path in sorted(folder.glob('*-*/metadata.json')):
                data = json.loads(path.read_text())
                width, height = (int(data['Viewport'][axis]) for axis in ('X', 'Y'))
                hdr, distance = floats(path.parent / 'hdr.f32'), floats(path.parent / 'distance.f32')
                assert len(hdr) == width * height * 4
                assert len(distance) == width * height
                luminances = [sum(hdr[4 * pixel + channel] * weight for channel, weight in enumerate(WEIGHTS))
                    for y in range(0, height, 2) for x in range(0, width, 2)
                    if distance[pixel := y * width + x] > 0]
                producing = data['Exposure']['Item2']
                lowest, count = (-16, 256) if tree == 'before' else (-160, 2304)
                expected = histogram(luminances, lowest, count, producing if tree == 'before' else 0)
                actual = json.loads((path.parent / 'histogram.json').read_text())
                assert expected == actual, str(path)
                physical = reading(histogram(luminances, -160, 2304), -160)
                estimator = reading(actual, lowest, producing if tree == 'before' else 0)
                rows.append(dict(tree=tree, mode=mode, checkpoint=path.parent.name,
                    active_size=[width, height], scene_allocation_size=[width, height], output_size=[160, 90],
                    scene_readback_floats=len(hdr), applied_stops=producing,
                    next_stops=data['Exposure']['Item1'], gpu_histogram_equals_cpu=True,
                    lowest_bin_count=actual[0], estimator=estimator, physical_scene_histogram=physical,
                    physical_unbinned_target_stops=exact_target(luminances),
                    hdr_sha256=hashlib.sha256((path.parent / 'hdr.f32').read_bytes()).hexdigest()))
            for control in ('auto', 'fixed0', 'fixed10'):
                path = ROOT / tree / (mode + '-' + control) / 'still-64/hdr.f32'
                other = ROOT / ('after' if tree == 'before' else 'before') / (mode + '-' + control) / 'still-64/hdr.f32'
                assert path.read_bytes() == other.read_bytes()
                identities.append(dict(tree=tree, mode=mode, control=control, both_pin_scene_bytes_equal=True,
                    hdr_sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
        for path in sorted((ROOT / 'source-isolation' / tree).glob('*/isolated')):
            hdr, diffuse, specular, emission = (floats(path / (name + '.f32'))
                for name in ('hdr', 'direct-diffuse', 'direct-specular', 'emission'))
            error = max(abs(hdr[i] - diffuse[i] - specular[i] - emission[i])
                for i in range(len(hdr)) if i % 4 != 3)
            assert error == 0
            assert json.loads((path.parent / 'validation.json').read_text()) == []
            source_controls.append(dict(tree=tree, case=path.parent.name, indirect_disabled=True,
                max_scene_rgb=max(hdr[i] for i in range(len(hdr)) if i % 4 != 3),
                max_scene_minus_direct_and_emission=error, validation_messages=0))
    result = dict(status='local investigation complete; no candidate production defect established',
        baseline='b002351eb48d33f008f8333e53e61345de3260cd',
        candidate='588b6d2747560c73aac542fb25ce80eec1ba8db7', tolerance_stops=.0625,
        platform='Mac Vulkan correctness only', rows=rows, settling=sequences,
        cross_pin_scene_identities=identities, source_isolation=source_controls,
        probe_metadata_correction='Initial probe metadata mislabeled the 160x90 output size as scene allocation size. Raw ReadLevel byte counts prove 96x54 scene allocation under fixed FSR and 160x90 under native. Initial raw files stay unchanged.',
        conclusion='The baseline clips dark pre-exposed samples into bin zero. The candidate retains scene samples. Default environment and volume add light even when the nominal direct source is below its visibility cutoff. Shared equality with the clipped baseline is an invalid physical oracle for this extreme fixture. The 1/16-stop tolerance remains unchanged.',
        limits=['Saved Linux artifacts lack raw HDR and histogram counts. The exact count diagnosis is local on the same immutable source pins.',
                'Readbacks add diagnostic overhead. No local result estimates Linux speed.',
                'A still makes two draws. Its applied exposure comes from the warmup draw, while the final histogram is from the shown draw at another jitter position.'])
    (ROOT / 'analysis.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(dict(checkpoints=len(rows), all_cpu_histograms_equal_gpu=True,
        validation_messages=0, summary=[row for row in rows if row['checkpoint'] == 'still-64']), indent=2))


if __name__ == '__main__':
    main()
